using System.Net.Http;
using System.Globalization;
using System.IO;
using System.Text.Json;
namespace FloatingDock;
public sealed record WeatherLocation(string Name,double Latitude,double Longitude)
{
    public static WeatherLocation Tokyo => new("东京",35.6762,139.6503);
}
public sealed record WeatherHour(DateTimeOffset Time,double Temperature,int Code,bool IsDay,double? RainProbability);
public sealed record WeatherDay(DateTimeOffset Time,double Minimum,double Maximum,int Code);
public sealed record WeatherReading(WeatherLocation Location,double Temperature,int Code,bool IsDay,DateTimeOffset UpdatedAt)
{
    public double? FeelsLike {get;init;}
    public double? Humidity {get;init;}
    public double? WindSpeed {get;init;}
    public int UtcOffsetSeconds {get;init;}
    public List<WeatherHour> Hourly {get;init;}=[];
    public List<WeatherDay> Daily {get;init;}=[];
}
public static class WeatherText
{
    public static string Condition(int code)=>code switch {
        0=>"晴",1=>"大部晴朗",2=>"多云",3=>"阴",45 or 48=>"雾",
        51=>"小毛毛雨",53=>"毛毛雨",55=>"强毛毛雨",56 or 57=>"冻毛毛雨",
        61=>"小雨",63=>"中雨",65=>"大雨",66 or 67=>"冻雨",
        71=>"小雪",73=>"中雪",75=>"大雪",77=>"雪粒",
        80=>"小阵雨",81=>"阵雨",82=>"强阵雨",85 or 86=>"阵雪",95=>"雷雨",96 or 99=>"雷雨伴冰雹",_=>"天气未知"
    };
    public static string Symbol(int code,bool day)=>code switch {0 or 1=>day?"☀":"☾",2 or 3=>"☁",45 or 48=>"≋",>=51 and <=67 or >=80 and <=82=>"☂",>=71 and <=77 or 85 or 86=>"❄",>=95 and <=99=>"ϟ",_=>"◇"};
    public static string Summary(WeatherReading reading,DateTimeOffset now)=>$"{reading.Location.Name}  {Math.Round(reading.Temperature,MidpointRounding.AwayFromZero):0}°  {Condition(reading.Code)}"+(now-reading.UpdatedAt>TimeSpan.FromMinutes(45)?" · 旧数据":"");
}
public sealed class WeatherService(HttpClient? client=null)
{
    static readonly HttpClient Shared=new(){Timeout=TimeSpan.FromSeconds(12)};
    readonly HttpClient http=client??Shared;
    public async Task<WeatherReading> GetAsync(WeatherLocation location,CancellationToken token=default)
    {
        var url=FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude}&longitude={location.Longitude}&current=temperature_2m,apparent_temperature,relative_humidity_2m,wind_speed_10m,weather_code,is_day&hourly=temperature_2m,weather_code,is_day,precipitation_probability&daily=temperature_2m_max,temperature_2m_min,weather_code&temperature_unit=celsius&wind_speed_unit=kmh&timeformat=unixtime&forecast_days=5&timezone=auto");
        var text=await http.GetStringAsync(url,token).ConfigureAwait(false);
        try {
            using var json=JsonDocument.Parse(text);var current=json.RootElement.GetProperty("current");
            var temperature=current.GetProperty("temperature_2m").GetDouble();
            if(!double.IsFinite(temperature)||temperature is < -100 or > 100)throw new InvalidDataException("天气温度无效。");
            var root=json.RootElement;
            int offset=(int)(Number(root,"utc_offset_seconds",-50400,50400)??0);offset-=offset%60;
            var hours=new List<WeatherHour>();var days=new List<WeatherDay>();
            if(root.TryGetProperty("hourly",out var hourly)&&hourly.ValueKind==JsonValueKind.Object)
            for(int i=0;i<120;i++){
                var t=Element(hourly,"time",i);var temp=ArrayNumber(hourly,"temperature_2m",i,-100,100);var code=ArrayNumber(hourly,"weather_code",i,0,999);var day=ArrayNumber(hourly,"is_day",i,0,1);
                if(Time(t,offset) is DateTimeOffset at&&temp is double value&&code is double c&&day is double d)hours.Add(new(at,value,(int)c,d==1,ArrayNumber(hourly,"precipitation_probability",i,0,100)));
            }
            if(root.TryGetProperty("daily",out var daily)&&daily.ValueKind==JsonValueKind.Object)
            for(int i=0;i<5;i++){
                var min=ArrayNumber(daily,"temperature_2m_min",i,-100,100);var max=ArrayNumber(daily,"temperature_2m_max",i,-100,100);var code=ArrayNumber(daily,"weather_code",i,0,999);
                if(Time(Element(daily,"time",i),offset) is DateTimeOffset at&&min is double a&&max is double b&&a<=b&&code is double c)days.Add(new(at,a,b,(int)c));
            }
            return new WeatherReading(location,temperature,current.GetProperty("weather_code").GetInt32(),current.GetProperty("is_day").GetInt32()==1,DateTimeOffset.UtcNow){FeelsLike=Number(current,"apparent_temperature",-100,100),Humidity=Number(current,"relative_humidity_2m",0,100),WindSpeed=Number(current,"wind_speed_10m",0,500),UtcOffsetSeconds=offset,Hourly=hours,Daily=days};
        }catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){throw new InvalidDataException("天气服务返回的数据不完整。",ex);}
    }
    static double? Number(JsonElement obj,string key,double min,double max)=>obj.ValueKind==JsonValueKind.Object&&obj.TryGetProperty(key,out var value)?ValidNumber(value,min,max):null;
    static double? ValidNumber(JsonElement value,double min,double max)=>value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out double n)&&double.IsFinite(n)&&n>=min&&n<=max?n:null;
    static JsonElement Element(JsonElement obj,string key,int index)=>obj.TryGetProperty(key,out var array)&&array.ValueKind==JsonValueKind.Array&&index<array.GetArrayLength()?array[index]:default;
    static double? ArrayNumber(JsonElement obj,string key,int index,double min,double max)=>ValidNumber(Element(obj,key,index),min,max);
    static DateTimeOffset? Time(JsonElement value,int offset){if(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt64(out long seconds)||seconds< -62135596800||seconds>253402250399)return null;try{return DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(TimeSpan.FromSeconds(offset));}catch(ArgumentException){return null;}}
    public async Task<IReadOnlyList<WeatherLocation>> SearchAsync(string city,CancellationToken token=default)
    {
        if(string.IsNullOrWhiteSpace(city))return [];
        var text=await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?name="+Uri.EscapeDataString(city.Trim())+"&count=5&language=zh&format=json",token).ConfigureAwait(false);
        try {
            using var json=JsonDocument.Parse(text);
            if(!json.RootElement.TryGetProperty("results",out var results))return [];
            var places=new List<WeatherLocation>();
            foreach(var place in results.EnumerateArray()) {
                string name=place.GetProperty("name").GetString()??city;
                if(place.TryGetProperty("country",out var country))name+=" · "+country.GetString();
                double lat=place.GetProperty("latitude").GetDouble(),lon=place.GetProperty("longitude").GetDouble();
                if(double.IsFinite(lat)&&double.IsFinite(lon)&&lat is >= -90 and <=90&&lon is >= -180 and <=180)places.Add(new(name,lat,lon));
            }
            return places;
        }catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){throw new InvalidDataException("无法读取城市搜索结果。",ex);}
    }
}
