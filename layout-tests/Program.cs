using FloatingDock;
int failures=0;
void CheckFloatingPosition(){foreach(var area in new[]{new DockArea(76,8,1800,1040),new DockArea(-1500,8,1400,850),new DockArea(76,8,260,210)}){var p=DockLayout.FloatingPosition(area,Math.Min(940,area.Width),184,false);if(p.X<area.Left||p.Y<area.Top||p.Y+184>area.Bottom)throw new Exception("floating launcher unsafe");if(area.Height>300&&Math.Abs(area.Bottom-p.Y-184-28)>.1)throw new Exception("missing elevated bottom gap");}}
void Check(string name,Action action){try{action();Console.WriteLine("PASS "+name);}catch(Exception ex){failures++;Console.WriteLine("FAIL "+name+": "+ex.Message);}}
void Equal<T>(T want,T got){if(!Equals(want,got))throw new Exception($"expected {want}, got {got}");}
Check("floating dock leaves reference bottom gap within safe work area",CheckFloatingPosition);
Check("music default placement fits safe region without covering existing cards or Dock",()=>{
 foreach(var safe in new[]{new DockArea(76,8,1800,1040),new DockArea(-1500,8,1400,850),new DockArea(76,8,680,700),new DockArea(76,8,260,430)}){var dock=new DockArea(safe.Left,safe.Bottom-184,Math.Min(940,safe.Width),184);var widgets=DockLayout.Widgets(safe,dock);var occupied=new[]{widgets.Weather,widgets.Downloads,widgets.Folders};var m=DockLayout.MusicCard(safe,dock,occupied);if(m.Height==0)continue;if(m.Left<safe.Left||m.Right>safe.Right||m.Top<safe.Top||m.Bottom>safe.Bottom)throw new Exception("unsafe music placement");foreach(var r in occupied.Append(dock))if(m.Left<r.Right&&m.Right>r.Left&&m.Top<r.Bottom&&m.Bottom>r.Top)throw new Exception("music default covers another surface");}
});

Check("active launcher geometry keeps plates and reflected icons within safe width",()=>{
 foreach(double width in new[]{212d,316,420,700,940,1180}){
  var l=DockLayout.LauncherWidth(width);if(l.AppRight-l.AppLeft<48||l.AppLeft<38+l.GroupWidth||l.AppRight>l.Tools.Left-10||l.Tools.Right>width-8)throw new Exception("active launcher overlaps at "+width);
  if(width>=700&&l.Tools.Left+3*68+32>width-8)throw new Exception("system plates overflow");
 }
});
Check("template widgets fit independently above launcher with left taskbar reserved",()=>{
 foreach(var safe in new[]{new DockArea(76,8,1800,1040),new DockArea(-1500,8,1400,850),new DockArea(76,8,680,700),new DockArea(76,8,260,430)}){
 var dock=new DockArea(safe.Left,safe.Bottom-170,Math.Min(940,safe.Width),170);var layout=DockLayout.Widgets(safe,dock);
 var cards=new[]{layout.Weather,layout.Downloads,layout.Folders};foreach(var c in cards)if(c.Left<safe.Left||c.Right>safe.Right+.01||c.Top<safe.Top||c.Bottom>dock.Top)throw new Exception("unsafe default widget");
 for(int a=0;a<3;a++)for(int b=a+1;b<3;b++)if(cards[a].Height>0&&cards[b].Height>0&&cards[a].Left<cards[b].Right&&cards[b].Left<cards[a].Right&&cards[a].Top<cards[b].Bottom&&cards[b].Top<cards[a].Bottom)throw new Exception("widgets overlap");
 }
});
Check("all supported widths keep islands apart and leave a usable app viewport",()=>{
 foreach(double width in new[]{212d,316,420,700,940,1180}){var l=DockLayout.ForWidth(width);if(l.Info.Right+8>l.Apps.Left||l.Apps.Right+8>l.Tools.Left||l.Tools.Right>width-4||l.AppRight-l.AppLeft<48)throw new Exception("overlap at "+width);}
});
Check("visible left taskbar and autohide width stay outside the safe area",()=>{
 var monitor=new DockArea(0,0,1920,1080);var work=new DockArea(64,0,1856,1080);var a=DockLayout.SafeArea(monitor,work,64);if(a.Left<76)throw new Exception("visible bar covered");
 var b=DockLayout.SafeArea(monitor,monitor,64);if(b.Left<76)throw new Exception("autohide bar covered");
 var c=DockLayout.SafeArea(new(-1920,0,1920,1080),new(-1920,0,1920,1040),0);if(c.Left< -1908||c.Right> -8||c.Bottom>1032)throw new Exception("negative monitor bounds lost");
});
Check("file panel remains inside safe workspace even when anchor is at either edge",()=>{
 var area=new DockArea(88,0,1192,800);foreach(var x in new[]{88d,1200}){var p=DockLayout.Panel(area,x,730,32,32,860,620);if(p.Left<area.Left||p.Right>area.Right||p.Top<area.Top||p.Bottom>area.Bottom)throw new Exception("popup outside safe area");}
});
Check("group filtering preserves order names and old entries",()=>{
 var s=new DockState();s.Items.Add(new(){Id="a",Name="原名",Path=@"C:\A.lnk"});s.Items.Add(new(){Id="b",Name="游戏",Path=@"C:\B.lnk",Group="leisure"});
 Equal("a,b",string.Join(',',DockGroups.Visible(s).Select(i=>i.Id)));s.SelectedGroup="work";Equal("a",DockGroups.Visible(s).Single().Id);s.SelectedGroup="leisure";Equal("b",DockGroups.Visible(s).Single().Id);Equal("原名",s.Items[0].Name);
});
Check("application search precedes file results without duplicating the same path",()=>{
 var s=new[]{new DockItem{Name="开发工具",Path=@"C:\Apps\code.lnk"}};
 var files=new[]{new BrowserEntry("code.lnk",@"C:\Apps\code.lnk",false,DateTime.UtcNow,1),new BrowserEntry("开发说明.txt",@"C:\Notes\开发说明.txt",false,DateTime.UtcNow,1)};
 var r=FileSearch.Merge(s,files,"开发");Equal(2,r.Count);Equal("开发工具",r[0].Name);Equal("应用",r[0].Kind);
});
var root=Path.Combine(Directory.GetCurrentDirectory(),"work","layout-tests-"+Guid.NewGuid());Directory.CreateDirectory(root);
try{
 Check("today filter uses creation time rather than modification time",()=>{
  var old=Path.Combine(root,"旧文件.txt");var fresh=Path.Combine(root,"新文件.txt");File.WriteAllText(old,"old");File.WriteAllText(fresh,"new");var today=DateTime.Today.ToUniversalTime();File.SetCreationTimeUtc(old,today.AddDays(-2));File.SetLastWriteTimeUtc(old,DateTime.UtcNow);File.SetCreationTimeUtc(fresh,today.AddHours(1));
  var r=FileBrowser.Query(new([root],CreatedSinceUtc:today));Equal("新文件.txt",r.Entries.Single().Name);
 });
 Check("new group fields persist while legacy JSON stays compatible",()=>{
  var file=Path.Combine(root,"settings.json");File.WriteAllText(file,"""{"Items":[{"Id":"a","Name":"原名","Path":"C:\\Apps\\a.lnk"}]}""");var s=DockStore.Load(file);Equal("all",s.SelectedGroup);Equal("work",s.Items.Single().Group);s.SelectedGroup="leisure";s.Items[0].Group="leisure";DockStore.Save(file,s);Equal("leisure",DockStore.Load(file).Items[0].Group);
 });
 Check("today application search excludes old-created entries",()=>{
  var old=Path.Combine(root,"old-app.lnk");var fresh=Path.Combine(root,"new-app.lnk");File.WriteAllText(old,"old");File.WriteAllText(fresh,"new");var today=DateTime.Today.ToUniversalTime();File.SetCreationTimeUtc(old,today.AddDays(-2));File.SetCreationTimeUtc(fresh,today.AddHours(1));
  var result=FileSearch.Merge([new(){Name="old",Path=old},new(){Name="new",Path=fresh}],[],"",today);Equal(fresh,result.Single().Path);Equal(true,result[0].CreatedUtc>=today);
 });
 Check("application plus file search limit explicitly marks partial results",()=>{
  var apps=Enumerable.Range(0,30).Select(i=>new DockItem{Name="match app "+i,Path=@"C:\Apps\app"+i+".lnk"});
  var files=Enumerable.Range(0,190).Select(i=>new BrowserEntry("match file "+i,@"C:\Files\file"+i+".txt",false,DateTime.UtcNow,1)).ToArray();
  var result=FileSearch.Apply(apps,new(files,false,190,1),"match");Equal(200,result.Entries.Count);Equal(true,result.Incomplete);
  var small=FileSearch.Apply([],new(files[..3],false,3,1),"match");Equal(false,small.Incomplete);Equal(3,small.Entries.Count);
 });
}finally{Directory.Delete(root,true);}
Check("each expanded category filters exactly and survives config reload",()=>{
 var s=new DockState();var categories=new[]{"work","development","creative","system","social","leisure"};
 foreach(var g in categories)s.Items.Add(new(){Id=g,Name=g,Path=@"C:\Apps\"+g+".lnk",Group=g});
 var file=Path.Combine(Directory.GetCurrentDirectory(),"work","group-roundtrip-"+Guid.NewGuid()+".json");
 try{foreach(var g in categories){s.SelectedGroup=g;Equal(g,DockGroups.Visible(s).Single().Id);DockStore.Save(file,s);var r=DockStore.Load(file);Equal(g,r.SelectedGroup);Equal(g,r.Items.Single(i=>i.Id==g).Group);Equal(g,DockGroups.Visible(r).Single().Id);}}
 finally{File.Delete(file);}
});
Check("wide tools expose three fixed system shortcuts",()=>{if(DockLayout.ForWidth(1180).Tools.Width<280)throw new Exception("system buttons have no space");});
Check("cards stay inside safe area without covering top or bottom Dock",()=>{
 foreach(var safe in new[]{new DockArea(76,8,1800,1040),new DockArea(-1500,0,1450,800),new DockArea(76,0,680,480)})foreach(var top in new[]{true,false})foreach(var collapsed in new[]{true,false}){
 var dock=new DockArea(safe.Left+10,top?safe.Top:safe.Bottom-170,500,170);var c=DockLayout.Cards(safe,dock,collapsed,collapsed);
 foreach(var card in new[]{c.Downloads,c.Folders})if(card.Left<safe.Left||card.Right>safe.Right||card.Top<safe.Top||card.Bottom>safe.Bottom||!(card.Bottom<=dock.Top||card.Top>=dock.Bottom))throw new Exception("unsafe card geometry");
 }
});
Check("cards hide when even title bars cannot fit beside Dock",()=>{
 var cards=DockLayout.Cards(new(0,0,680,200),new(0,0,500,170),false,false);
 Equal(0d,cards.Downloads.Height);Equal(0d,cards.Folders.Height);
});
Check("card movement and every resize edge stay outside the left taskbar",()=>{
 foreach(var safe in new[]{new DockArea(76,8,1200,800),new DockArea(-1500,8,1400,850)})
 foreach(var edge in new[]{CardEdge.None,CardEdge.Left,CardEdge.Right,CardEdge.Top,CardEdge.Bottom,CardEdge.Left|CardEdge.Top,CardEdge.Right|CardEdge.Top,CardEdge.Left|CardEdge.Bottom,CardEdge.Right|CardEdge.Bottom})
 foreach(var delta in new[]{-10000d,10000}){
  var r=DockLayout.AdjustCard(new(safe.Left+120,safe.Top+70,304,286),safe,edge,delta,delta,false);
  if(r.Left<safe.Left||r.Top<safe.Top||r.Right>safe.Right||r.Bottom>safe.Bottom||r.Width<240||r.Height<180)throw new Exception("unsafe resize "+edge);
 }
});
Check("left top resize preserves opposite edges and minimum size",()=>{
 var r=DockLayout.AdjustCard(new(200,200,304,286),new(76,8,1200,800),CardEdge.Left|CardEdge.Top,10000,10000,false);
 Equal(504d,r.Right);Equal(486d,r.Bottom);Equal(240d,r.Width);Equal(180d,r.Height);
});
Check("collapsed resize keeps title height and small monitor recovery stays visible",()=>{
 var r=DockLayout.AdjustCard(new(200,200,304,48),new(76,8,1200,800),CardEdge.Right|CardEdge.Bottom,120,100,false);
 var c=DockLayout.AdjustCard(new(200,200,304,48),new(76,8,1200,800),CardEdge.Right|CardEdge.Bottom,120,100,true);Equal(48d,c.Height);Equal(424d,c.Width);
 var small=DockLayout.FitCard(new(-3000,-3000,1500,1500),new(88,8,200,150),false);Equal(200d,small.Width);Equal(150d,small.Height);Equal(88d,small.Left);
});
Check("independent card positions survive reload and invalid optional placement recovers",()=>{
 var p=Path.Combine(Directory.GetCurrentDirectory(),"work","placement-"+Guid.NewGuid()+".json");
 try{var s=new DockState();s.Files.DownloadsPlacement=new(){PixelX=100,PixelY=80,Width=420,Height=350};s.Files.FoldersPlacement=new(){PixelX=-1400,PixelY=100,Width=280,Height=240};DockStore.Save(p,s);var back=DockStore.Load(p);Equal(420d,back.Files.DownloadsPlacement.Width);Equal(-1400d,back.Files.FoldersPlacement.PixelX!.Value);Equal(350d,back.Files.DownloadsPlacement.Height);
 File.WriteAllText(p,"""{"Files":{"DownloadsPlacement":{"PixelX":100,"Width":-1,"Height":0},"FoldersPlacement":null},"Items":[]}""");var recovered=DockStore.Load(p);Equal(null,recovered.Files.DownloadsPlacement.PixelX);Equal(240d,recovered.Files.DownloadsPlacement.Width);Equal(180d,recovered.Files.DownloadsPlacement.Height);Equal(250d,recovered.Files.FoldersPlacement.Height);
 }finally{File.Delete(p);}
});
Check("card visibility persists independently and legacy shared setting migrates",()=>{
 var p=Path.Combine(Directory.GetCurrentDirectory(),"work","card-visible-"+Guid.NewGuid()+".json");
 try{
  File.WriteAllText(p,"""{"Files":{"DesktopCards":false},"Items":[]}""");var s=DockStore.Load(p);Equal(false,s.Files.CardVisible(true));Equal(false,s.Files.CardVisible(false));
  s.Files.DownloadsPlacement=new(){PixelX=120,PixelY=80,Width=420,Height=330};s.Files.FoldersPlacement=new(){PixelX=700,PixelY=60,Width=320,Height=290};
  s.Files.SetCardVisible(true,true);DockStore.Save(p,s);var r=DockStore.Load(p);Equal(true,r.Files.CardVisible(true));Equal(false,r.Files.CardVisible(false));Equal(420d,r.Files.DownloadsPlacement.Width);Equal(700d,r.Files.FoldersPlacement.PixelX!.Value);
  r.Files.SetCardVisible(true,false);r.Files.SetCardVisible(false,true);DockStore.Save(p,r);var back=DockStore.Load(p);Equal(false,back.Files.CardVisible(true));Equal(true,back.Files.CardVisible(false));Equal(330d,back.Files.DownloadsPlacement.Height);Equal(320d,back.Files.FoldersPlacement.Width);
 }finally{File.Delete(p);}
});
Console.WriteLine($"Failures: {failures}");return failures==0?0:1;



