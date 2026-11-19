using System.Text.Json;
using System.IO;
namespace FloatingDock;
public static class DockGroupIds {
 public static IReadOnlyList<string> Values {get;}=Array.AsReadOnly(new[]{"development","creative","work","system","social","leisure"});
 public static bool Valid(string? id,bool allowAll=false)=>(allowAll&&id=="all")||(id is not null&&Values.Contains(id));
}
public sealed class DockItem { public string Group {get;set;}="work"; public string Id {get;set;}=Guid.NewGuid().ToString("N"); public string Name {get;set;}=""; public string Path {get;set;}=""; }
public sealed class WeatherWidgetSettings {public bool Visible {get;set;}=true;public bool Collapsed {get;set;}public DesktopCardPlacement Placement {get;set;}=new(){Width=380,Height=392};}
public sealed class MusicWidgetSettings {public bool Visible {get;set;}=true;public bool Collapsed {get;set;}public DesktopCardPlacement Placement {get;set;}=new(){Width=304,Height=216};}
public static class DockVisualUpgrade
{
 public static bool Apply(DockState state){if(state.VisualStyleVersion>=1)return false;state.Width=Math.Min(state.Width,940);state.VisualStyleVersion=1;return true;}
}
public static class WidgetTemplate
{
 public static void Apply(DockState state)
 {
  if(state.WidgetLayoutVersion>=1)return;
  state.Files.DownloadsPlacement=new(){Width=304,Height=200};state.Files.FoldersPlacement=new(){Width=304,Height=180};
  state.Files.DownloadsCollapsed=state.Files.FoldersCollapsed=false;state.WidgetLayoutVersion=1;
 }
}
public sealed class DesktopCardPlacement { public bool AutoCollapsed {get;set;} public double? PixelX {get;set;} public double? PixelY {get;set;} public double Width {get;set;}=304; public double Height {get;set;}=286; public void Normalize(){if(PixelX is not double x||PixelY is not double y||!double.IsFinite(x)||!double.IsFinite(y)){PixelX=PixelY=null;} Width=double.IsFinite(Width)?Math.Clamp(Width,240,32768):304;Height=double.IsFinite(Height)?Math.Clamp(Height,180,32768):286;} }
public sealed class FilePanelSettings {
 public string? ProjectDropFolder {get;set;}
 public bool QuickDropCollapsed {get;set;}
 public bool? DownloadsVisible {get;set;} public bool? FoldersVisible {get;set;} public bool CardVisible(bool downloads)=>(downloads?DownloadsVisible:FoldersVisible)??DesktopCards; public void SetCardVisible(bool downloads,bool visible){if(downloads)DownloadsVisible=visible;else FoldersVisible=visible;} public DesktopCardPlacement DownloadsPlacement {get;set;}=new(); public DesktopCardPlacement FoldersPlacement {get;set;}=new(){Height=250}; public bool DesktopCards {get;set;}=true; public bool DownloadsCollapsed {get;set;} public bool FoldersCollapsed {get;set;} public string SelectedSource {get;set;}="recent"; public bool NameSort {get;set;} public List<string> AdditionalFolders {get;set;}=[]; }
public sealed class DockState {
 public List<string> FavoriteIds {get;set;}=[];
 public bool DockCollapsed {get;set;}
 public MusicWidgetSettings MusicWidget {get;set;}=new(); public int VisualStyleVersion {get;set;} public int WidgetLayoutVersion {get;set;} public WeatherWidgetSettings WeatherWidget {get;set;}=new(); public string SelectedGroup {get;set;}="all"; public bool AutoImportDesktop {get;set;}=true; public List<string> IgnoredDesktopShortcuts {get;set;}=[]; public FilePanelSettings Files {get;set;}=new(); public string CollectionPath {get;set;}=""; public List<DockItem> Items {get;set;}=[]; public double? X {get;set;} public double? Y {get;set;} public double? PixelX {get;set;} public double? PixelY {get;set;} public bool TopEdge {get;set;} public double Width {get;set;}=940; public WeatherLocation Location {get;set;}=WeatherLocation.Tokyo; public WeatherReading? Weather {get;set;} }
public static class DockMath {
 public const double Slot=68;
 public static double ClampScroll(double value,int count,double viewport)=>Math.Clamp(double.IsFinite(value)?value:0,0,Math.Max(0,count*Slot-Math.Max(0,viewport)));
 public static double Magnification(double distance,bool active) { if(!active||!double.IsFinite(distance)||Math.Abs(distance)>=160)return 1;return 1+0.60*(1+Math.Cos(Math.PI*Math.Abs(distance)/160))/2; }
 public static (double X,double Y) KeepVisible(double x,double y,double width,double height,double left,double top,double areaWidth,double areaHeight)=>
 (Math.Clamp(double.IsFinite(x)?x:left,left,left+Math.Max(0,areaWidth-width)), Math.Clamp(double.IsFinite(y)?y:top,top,top+Math.Max(0,areaHeight-height)));
}
public static class DockCatalog {
 public static int ImportDesktop(DockState state,IEnumerable<string> paths) {
  if(!state.AutoImportDesktop)return 0;
  int before=state.Items.Count;var ignored=new HashSet<string>(state.IgnoredDesktopShortcuts,StringComparer.OrdinalIgnoreCase);
  Merge(state,paths.Where(p=>!ignored.Contains(p)));return state.Items.Count-before;
 }
 public static void Merge(DockState state,IEnumerable<string> paths) {
  var seen=new HashSet<string>(state.Items.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);
  foreach(var path in paths) { if(string.IsNullOrWhiteSpace(path))continue;var full=System.IO.Path.GetFullPath(path);if(seen.Add(full)) state.Items.Add(new DockItem{Name=System.IO.Path.GetFileNameWithoutExtension(full),Path=full}); }
 }
}
public static class DockStore {
 static readonly JsonSerializerOptions Options=new(){WriteIndented=true};
 public static DockState Load(string path) {
  if(!File.Exists(path))return new DockState();
  try {
   var s=JsonSerializer.Deserialize<DockState>(File.ReadAllText(path),Options)??throw new JsonException("Empty state");
   s.WeatherWidget??=new();s.WeatherWidget.Placement??=new(){Width=380,Height=392};s.WeatherWidget.Placement.Normalize();
   s.MusicWidget??=new();s.MusicWidget.Placement??=new(){Width=304,Height=216};s.MusicWidget.Placement.Normalize();
   s.Files??=new();
   if(s.Files.ProjectDropFolder is not { Length: > 0 } projectFolder || !Path.IsPathFullyQualified(projectFolder))
    s.Files.ProjectDropFolder=null;
   s.Files.DownloadsVisible??=s.Files.DesktopCards;s.Files.FoldersVisible??=s.Files.DesktopCards;
   s.Files.DownloadsPlacement??=new();s.Files.FoldersPlacement??=new(){Height=250};s.Files.DownloadsPlacement.Normalize();s.Files.FoldersPlacement.Normalize();
   if(!DockGroupIds.Valid(s.SelectedGroup,true))s.SelectedGroup="all";
   s.IgnoredDesktopShortcuts=(s.IgnoredDesktopShortcuts??[]).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).Take(4096).ToList();
   if(string.IsNullOrWhiteSpace(s.Files.SelectedSource))s.Files.SelectedSource="recent";
   var folders=new List<string>();
   foreach(var folder in (s.Files.AdditionalFolders??[]).Take(32)) {
    if(string.IsNullOrWhiteSpace(folder))continue;
    try {var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));if(!folders.Contains(full,StringComparer.OrdinalIgnoreCase))folders.Add(full);}
    catch(Exception ex) when(ex is ArgumentException or NotSupportedException or PathTooLongException) { }
   }
   s.Files.AdditionalFolders=folders;
   if(s.Items is null||s.Items.Any(x=>x is null||string.IsNullOrWhiteSpace(x.Path)||string.IsNullOrWhiteSpace(x.Id)))throw new JsonException("Invalid items");
   foreach(var item in s.Items)if(!DockGroupIds.Valid(item.Group))item.Group="work";
   s.Items=s.Items.DistinctBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).ToList();
   var itemIds=s.Items.Select(item=>item.Id).ToHashSet(StringComparer.Ordinal);
   s.FavoriteIds=(s.FavoriteIds??[]).Where(itemIds.Contains).Distinct(StringComparer.Ordinal).ToList();
   s.Width=double.IsFinite(s.Width)?Math.Clamp(s.Width,420,1400):940;
   if(s.X is double x&&!double.IsFinite(x))s.X=null;if(s.Y is double y&&!double.IsFinite(y))s.Y=null;
   if(s.Location is null||string.IsNullOrWhiteSpace(s.Location.Name)||!double.IsFinite(s.Location.Latitude)||!double.IsFinite(s.Location.Longitude)||s.Location.Latitude is < -90 or >90||s.Location.Longitude is < -180 or >180)s.Location=WeatherLocation.Tokyo;
   if(s.Weather is not null&&(s.Weather.Location!=s.Location||!double.IsFinite(s.Weather.Temperature)))s.Weather=null;
   return s;
  }catch(JsonException ex){throw new InvalidDataException("配置文件损坏，原文件已保留。",ex);}
 }
 public static void Save(string path,DockState state) {
  var full=System.IO.Path.GetFullPath(path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
  var temp=full+".tmp";
  File.WriteAllText(temp,JsonSerializer.Serialize(state,Options));
  File.Move(temp,full,true);
 }
}




