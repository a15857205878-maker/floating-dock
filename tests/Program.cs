using FloatingDock;
using System.Net;
using System.Net.Http;
int failures=0;
void Check(string name, Action test) { try { test(); Console.WriteLine("PASS "+name); } catch(Exception e) { failures++; Console.WriteLine("FAIL "+name+": "+e); } }
void Equal<T>(T expected,T actual) { if(!Equals(expected,actual)) throw new Exception($"expected {expected}, got {actual}"); }
Check("scroll cannot pass either edge",()=>{Equal(0d,DockMath.ClampScroll(-80,20,600)); Equal(760d,DockMath.ClampScroll(5000,20,600));});
Check("empty and short lists never scroll",()=>{Equal(0d,DockMath.ClampScroll(500,0,600));Equal(0d,DockMath.ClampScroll(500,3,600));});
Check("magnification falls off continuously",()=>{if(!(DockMath.Magnification(0,true)>DockMath.Magnification(60,true)&&DockMath.Magnification(60,true)>DockMath.Magnification(140,true))) throw new Exception("no distance falloff");Equal(1d,DockMath.Magnification(500,true));Equal(1d,DockMath.Magnification(0,false));});
Check("offscreen window returns to negative-coordinate monitor",()=>{var p=DockMath.KeepVisible(8000,-9000,900,190,-1920,0,1920,1080);Equal(-900d,p.X);Equal(0d,p.Y);});
Check("merge case-insensitive paths preserves renamed items",()=>{var s=new DockState();s.Items.Add(new DockItem{Name="自定义",Path=@"C:\Apps\A.lnk"});DockCatalog.Merge(s, new[]{@"c:\apps\a.LNK",@"C:\Apps\B.lnk"});Equal(2,s.Items.Count);Equal("自定义",s.Items[0].Name);});
var dir=Path.Combine(Directory.GetCurrentDirectory(),"work","dock-tests-"+Guid.NewGuid());Directory.CreateDirectory(dir);
try {
 Check("collection keeps folders files and Chinese names without entering subfolders",()=>{var folder=Path.Combine(dir,"收纳");Directory.CreateDirectory(Path.Combine(folder,"资料"));File.WriteAllText(Path.Combine(folder,"资料","内部.txt"),"nested");File.WriteAllText(Path.Combine(folder,"笔记.txt"),"note");File.WriteAllText(Path.Combine(folder,"desktop.ini"),"metadata");var entries=FileCollection.Read(folder);Equal(2,entries.Count);Equal(true,entries[0].IsDirectory);Equal("资料",entries[0].Name);Equal("笔记.txt",entries[1].Name);Equal(Path.Combine(folder,"笔记.txt"),entries[1].Path);Equal(0,FileCollection.Read(Path.Combine(dir,"missing")).Count);});
 Check("state persists Chinese paths order and window placement",()=>{var s=new DockState{X=-1800,Y=40,PixelX=-3600,PixelY=80,TopEdge=true};s.Items.Add(new DockItem{Name="中文",Path=@"C:\软件\A.lnk"});s.Items.Add(new DockItem{Name="第二",Path=@"C:\软件\B.lnk"});var f=Path.Combine(dir,"state.json");DockStore.Save(f,s);var r=DockStore.Load(f);Equal("中文",r.Items[0].Name);Equal("第二",r.Items[1].Name);Equal(-1800d,r.X!.Value);Equal(-3600d,r.PixelX!.Value);Equal(80d,r.PixelY!.Value);Equal(true,r.TopEdge);s.Items.Reverse();DockStore.Save(f,s);Equal("第二",DockStore.Load(f).Items[0].Name);});

 Check("favorites and folded state persist without changing the catalog",()=>{
  var a=new DockItem{Id="station",Name="个人工作站",Path=@"C:\Desk\A.lnk",Group="work"};
  var b=new DockItem{Id="dragon",Name="龙娘桌宠",Path=@"C:\Desk\B.lnk",Group="social"};
  var s=new DockState{Items=[a,b],FavoriteIds=["dragon","missing","station","dragon"],DockCollapsed=true,X=313,Y=424};
  var path=Path.Combine(dir,"favorites.json");DockStore.Save(path,s);var read=DockStore.Load(path);
  Equal("dragon,station",string.Join(',',read.FavoriteIds));Equal("station,dragon",string.Join(',',read.Items.Select(i=>i.Id)));
  Equal("social",read.Items[1].Group);Equal(true,read.DockCollapsed);Equal(313d,read.X);
 });
 Check("damaged state is preserved before recovery",()=>{var f=Path.Combine(dir,"bad.json");File.WriteAllText(f,"{broken");try {DockStore.Load(f);throw new Exception("expected InvalidDataException");}catch(InvalidDataException){} Equal("{broken",File.ReadAllText(f));});
}finally{Directory.Delete(dir,true);}
Check("weather request parses real current fields and Celsius",()=>{
 var handler=new FixtureHandler("""{"current":{"temperature_2m":18.7,"weather_code":61,"is_day":0}}""");
 var r=new WeatherService(new HttpClient(handler)).GetAsync(WeatherLocation.Tokyo).GetAwaiter().GetResult();
 Equal(18.7,r.Temperature);Equal(61,r.Code);Equal(false,r.IsDay);Equal("东京",r.Location.Name);
 if(handler.Uri?.Host!="api.open-meteo.com"||!handler.Uri.Query.Contains("latitude=35.6762")||!handler.Uri.Query.Contains("temperature_2m"))throw new Exception("wrong forecast request");
});
Check("missing weather temperature cannot become zero",()=>{
 try{new WeatherService(new HttpClient(new FixtureHandler("""{"current":{"temperature_2m":null,"weather_code":0,"is_day":1}}"""))).GetAsync(WeatherLocation.Tokyo).GetAwaiter().GetResult();throw new Exception("accepted missing temperature");}catch(InvalidDataException){}
});
Check("weather codes distinguish fog rain snow and thunder",()=>{Equal("雾",WeatherText.Condition(45));Equal("小雨",WeatherText.Condition(61));Equal("大雪",WeatherText.Condition(75));Equal("雷雨",WeatherText.Condition(95));Equal("天气未知",WeatherText.Condition(999));if(WeatherText.Symbol(0,true)==WeatherText.Symbol(0,false))throw new Exception("day night same");});
Check("stale weather is marked rather than shown as current",()=>{var at=DateTimeOffset.Parse("2026-11-01T00:00:00Z");var r=new WeatherReading(WeatherLocation.Tokyo,18.7,61,true,at);Equal("东京  19°  小雨",WeatherText.Summary(r,at.AddMinutes(10)));if(!WeatherText.Summary(r,at.AddMinutes(50)).Contains("旧数据"))throw new Exception("stale reading unmarked");});
Check("city search encodes names and returns selectable coordinates",()=>{var h=new FixtureHandler("""{"results":[{"name":"东京","latitude":35.68,"longitude":139.69,"country":"日本"}]}""");var r=new WeatherService(new HttpClient(h)).SearchAsync("Tokyo & test").GetAwaiter().GetResult();Equal(1,r.Count);Equal("东京 · 日本",r[0].Name);Equal(35.68,r[0].Latitude);if(h.Uri is null||!h.Uri.Query.Contains("%26"))throw new Exception("city not encoded");});
Check("weather HTTP failure is not fabricated as sunshine",()=>{try{new WeatherService(new HttpClient(new FixtureHandler("{}",HttpStatusCode.ServiceUnavailable))).GetAsync(WeatherLocation.Tokyo).GetAwaiter().GetResult();throw new Exception("accepted HTTP error");}catch(HttpRequestException){}});
Check("acrylic fill crops wallpaper around the monitor center",()=>{Equal(new MaterialRect(-420,0,1920,1080),AcrylicGeometry.WallpaperBounds(1920,1080,new(0,0,1080,1080),WallpaperPlacement.Fill,new(0,0,1080,1080)));});
Check("acrylic fit preserves wallpaper letterboxing",()=>{Equal(new MaterialRect(0,236.25,1080,607.5),AcrylicGeometry.WallpaperBounds(1920,1080,new(0,0,1080,1080),WallpaperPlacement.Fit,new(0,0,1080,1080)));});
Check("acrylic sampling follows negative-coordinate displays",()=>{Equal(new MaterialRect(-1520,390,800,600),AcrylicGeometry.WallpaperBounds(800,600,new(-1920,150,1600,1080),WallpaperPlacement.Center,new(-1920,0,3840,1230)));});
Check("acrylic spanning wallpaper uses the whole virtual desktop",()=>{Equal(new MaterialRect(-1920,0,3840,1080),AcrylicGeometry.WallpaperBounds(3840,1080,new(0,0,1920,1080),WallpaperPlacement.Span,new(-1920,0,3840,1080)));});
Check("rich weather keeps missing data nullable and city offset",()=>{
 var h=new FixtureHandler("""{"utc_offset_seconds":32400,"current":{"temperature_2m":19,"weather_code":2,"is_day":1,"apparent_temperature":18,"relative_humidity_2m":56,"wind_speed_10m":8},"hourly":{"time":[1794614400,1794618000],"temperature_2m":[20,null],"weather_code":[0,2],"is_day":[1,1],"precipitation_probability":[10,20]},"daily":{"time":[1794614400],"temperature_2m_max":[21],"temperature_2m_min":[14],"weather_code":[2]}}""");
 var r=new WeatherService(new HttpClient(h)).GetAsync(WeatherLocation.Tokyo).GetAwaiter().GetResult();Equal(18d,r.FeelsLike);Equal(56d,r.Humidity);Equal(8d,r.WindSpeed);Equal(32400,r.UtcOffsetSeconds);Equal(1,r.Hourly.Count);Equal(1,r.Daily.Count);Equal(21d,r.Daily[0].Maximum);
 if(!h.Uri!.Query.Contains("timeformat=unixtime")||!h.Uri.Query.Contains("wind_speed_unit=kmh"))throw new Exception("ambiguous units or forecast timezone");
});
Check("legacy weather and invalid optional values never invent readings",()=>{
 var r=new WeatherService(new HttpClient(new FixtureHandler("""{"current":{"temperature_2m":19,"weather_code":2,"is_day":1,"relative_humidity_2m":150,"wind_speed_10m":null},"hourly":{"time":[1],"temperature_2m":[]}}"""))).GetAsync(WeatherLocation.Tokyo).GetAwaiter().GetResult();Equal(null,r.Humidity);Equal(null,r.WindSpeed);Equal(0,r.Hourly.Count);Equal(0,r.Daily.Count);
});
Check("weather hide and size roundtrip independently from file cards",()=>{
 var file=Path.Combine(Directory.GetCurrentDirectory(),"work","weather-state-"+Guid.NewGuid()+".json");
 try {var s=new DockState();s.WeatherWidget.Visible=false;s.WeatherWidget.Placement=new(){PixelX=-1200,PixelY=80,Width=420,Height=410};s.Files.SetCardVisible(true,true);DockStore.Save(file,s);var r=DockStore.Load(file);Equal(false,r.WeatherWidget.Visible);Equal(-1200d,r.WeatherWidget.Placement.PixelX);Equal(420d,r.WeatherWidget.Placement.Width);Equal(true,r.Files.CardVisible(true));}finally{File.Delete(file);}
});
Check("template migration preserves hidden cards and never resets later placement",()=>{
 var s=new DockState();s.Files.SetCardVisible(true,false);s.Files.DownloadsCollapsed=true;s.Files.DownloadsPlacement.PixelX=2000;s.Items.Add(new(){Id="keep",Name="保留",Path=@"C:\Apps\a.lnk",Group="creative"});
 WidgetTemplate.Apply(s);Equal(1,s.WidgetLayoutVersion);Equal(false,s.Files.CardVisible(true));Equal(false,s.Files.DownloadsCollapsed);Equal(null,s.Files.DownloadsPlacement.PixelX);Equal("keep",s.Items[0].Id);Equal("creative",s.Items[0].Group);
 s.Files.DownloadsPlacement.PixelX=500;s.WeatherWidget.Visible=false;WidgetTemplate.Apply(s);Equal(500d,s.Files.DownloadsPlacement.PixelX);Equal(false,s.WeatherWidget.Visible);
});
Check("visual upgrade compacts only launcher once and preserves widget state",()=>{
 var s=new DockState{Width=1180,WidgetLayoutVersion=1};s.Files.DownloadsPlacement.PixelX=1385;s.Files.SetCardVisible(false,false);s.WeatherWidget.Placement.Width=430;s.WeatherWidget.Visible=false;
 Equal(true,DockVisualUpgrade.Apply(s));Equal(940d,s.Width);Equal(1385d,s.Files.DownloadsPlacement.PixelX);Equal(false,s.Files.CardVisible(false));Equal(false,s.WeatherWidget.Visible);Equal(430d,s.WeatherWidget.Placement.Width);Equal(1,s.VisualStyleVersion);
 s.Width=1180;Equal(false,DockVisualUpgrade.Apply(s));Equal(1180d,s.Width);
});
Check("music session filters other players and bounds metadata",()=>{
 Equal(false,MusicSnapshot.IsNetEase("org.netease.youdao!App"));Equal(false,MusicSnapshot.IsNetEase("Spotify.exe"));Equal(false,MusicSnapshot.IsNetEase("chrome.exe"));Equal(true,MusicSnapshot.IsNetEase("cloudmusic.exe"));Equal(true,MusicSnapshot.IsNetEase("org.netease.cloudmusic!App"));
 Equal(false,MusicSnapshot.Parse("""{"Available":true,"Source":"Spotify.exe","Title":"other"}""").Available);
 var song=MusicSnapshot.Parse("""{"Available":true,"Source":"cloudmusic.exe","Title":"测试歌曲","Artist":"测试歌手","Duration":180,"Position":999,"CanSeek":true}""");Equal("测试歌曲",song.Title);Equal(180d,song.Position);Equal(true,song.CanSeek);
});
Check("music album metadata is bounded optional and backwards compatible",()=>{
 var song=MusicSnapshot.Parse("""{"Available":true,"Source":"cloudmusic.exe","Album":"测试专辑"}""");
 Equal("测试专辑",typeof(MusicSnapshot).GetProperty("Album")?.GetValue(song) as string);
 var longReply=System.Text.Json.JsonSerializer.Serialize(new{Available=true,Source="cloudmusic.exe",Album=new string('专',800)});
 Equal(500,(typeof(MusicSnapshot).GetProperty("Album")?.GetValue(MusicSnapshot.Parse(longReply)) as string)?.Length??-1);
 Equal("",typeof(MusicSnapshot).GetProperty("Album")?.GetValue(MusicSnapshot.Parse("""{"Available":true,"Source":"cloudmusic.exe","Album":null}""")) as string);
 Equal("",typeof(MusicSnapshot).GetProperty("Album")?.GetValue(MusicSnapshot.Parse("""{"Available":true,"Source":"cloudmusic.exe"}""")) as string);
});
Check("music independent settings migrate without resetting existing widgets",()=>{
 var path=Path.Combine(Directory.GetCurrentDirectory(),"work","music-state-"+Guid.NewGuid()+".json");try{var s=new DockState();s.WeatherWidget.Visible=false;s.Files.DownloadsPlacement.PixelX=1000;s.Files.DownloadsPlacement.PixelY=40;s.MusicWidget.Visible=false;s.MusicWidget.Placement=new(){PixelX=-800,PixelY=70,Width=330,Height=240};DockStore.Save(path,s);var r=DockStore.Load(path);Equal(false,r.MusicWidget.Visible);Equal(-800d,r.MusicWidget.Placement.PixelX);Equal(240d,r.MusicWidget.Placement.Height);Equal(false,r.WeatherWidget.Visible);Equal(1000d,r.Files.DownloadsPlacement.PixelX);File.WriteAllText(path,"""{"Items":[],"MusicWidget":null}""");Equal(true,DockStore.Load(path).MusicWidget.Visible);}finally{File.Delete(path);}
});
Check("music worker filter matches only actual CloudMusic identities",()=>{
 var script=File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(),"scripts","NetEaseMediaBridge.ps1"));var pattern=System.Text.RegularExpressions.Regex.Match(script,"-match '([^']+)'").Groups[1].Value;if(pattern.Length==0)throw new Exception("worker target predicate missing");
 foreach(var source in new[]{"Spotify.exe","org.netease.youdao!App","netease.game!player","chrome.exe"})if(System.Text.RegularExpressions.Regex.IsMatch(source,pattern))throw new Exception("worker selects foreign session "+source);
 if(!System.Text.RegularExpressions.Regex.IsMatch("cloudmusic.exe",pattern))throw new Exception("worker misses actual desktop client");
});
Console.WriteLine($"Failures: {failures}");return failures==0?0:1;

sealed class FixtureHandler(string json,HttpStatusCode status=HttpStatusCode.OK):HttpMessageHandler
{
 public Uri? Uri;
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){Uri=request.RequestUri;return Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(json)});}
}
