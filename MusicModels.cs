using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;

namespace FloatingDock;
public sealed record MusicSnapshot
{
 public bool Available {get;init;}
 public string Source {get;init;}="";
 public string Title {get;init;}="";
 public string Artist {get;init;}="";
 public string Album {get;init;}="";
 public string State {get;init;}="";
 public bool CanToggle {get;init;}
 public bool CanPrevious {get;init;}
 public bool CanNext {get;init;}
 public bool CanSeek {get;init;}
 public double Position {get;init;}
 public double Duration {get;init;}
 public string Cover {get;init;}="";
 public static bool IsNetEase(string? id)=>id is not null&&Regex.IsMatch(id,@"(^|[\\/.!_-])(cloudmusic)(\.exe|[.!_-]|$)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
 public static MusicSnapshot Parse(string json)
 {
  if(json.Length>450000)throw new InvalidDataException("媒体回复过大");
  var r=JsonSerializer.Deserialize<MusicSnapshot>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new();
  if(!r.Available||!IsNetEase(r.Source))return new();
  double duration=double.IsFinite(r.Duration)?Math.Clamp(r.Duration,0,86400):0;
  return r with{Title=Trim(r.Title,500),Artist=Trim(r.Artist,500),Album=Trim(r.Album,500),State=Trim(r.State,32),Duration=duration,Position=double.IsFinite(r.Position)?Math.Clamp(r.Position,0,duration):0,Cover=r.Cover is {Length:<=350000}?r.Cover:"",CanSeek=r.CanSeek&&duration>0};
 }
 static string Trim(string? value,int length)=>string.IsNullOrWhiteSpace(value)?"":value[..Math.Min(length,value.Length)];
}
