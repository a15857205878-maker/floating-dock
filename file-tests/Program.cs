using FloatingDock;

int failures = 0;
void Check(string name, Action test) { try { test(); Console.WriteLine("PASS " + name); } catch(Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
void Equal<T>(T want, T got) { if (!Equals(want, got)) throw new Exception($"expected {want}, got {got}"); }
var scratch = Path.Combine(Directory.GetCurrentDirectory(), "work", "file-tests-" + Guid.NewGuid());
Directory.CreateDirectory(scratch);
string Root(string name) { var p = Path.GetFullPath(Path.Combine(scratch, name)); Directory.CreateDirectory(p); return p; }
string Write(string root, string name, int minutes) { var p = Path.Combine(root, name); File.WriteAllText(p, "fixture"); File.SetLastWriteTimeUtc(p, new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes)); return p; }
try {
    Check("recent files merge sources and sort by modification rather than name", () => {
        var a=Root("desktop");var b=Root("downloads");Write(a,"甲.txt",1);var latest=Write(b,"乙.pdf",3);Write(a,"丙.txt",2);
        var r=FileBrowser.Query(new([a,b], FilesOnly:true));Equal(3,r.Entries.Count);Equal(latest,r.Entries[0].Path);Equal(false,r.Incomplete);
    });
    Check("recursive Chinese search preserves same-name files in different folders", () => {
        var a=Root("search");Write(a,"报告.pdf",1);var sub=Root("search/资料");Write(sub,"报告.pdf",2);Write(sub,"其他.txt",3);
        var r=FileBrowser.Query(new([a],Search:"报告",Recursive:true));Equal(2,r.Entries.Count);Equal(2,r.Entries.Select(e=>e.Path).Distinct().Count());
    });
    Check("browsing a folder does not recursively flatten children", () => {
        var a=Root("browse");Root("browse/子目录");Write(Path.Combine(a,"子目录"),"内部.txt",1);Write(a,"外部.txt",2);
        var r=FileBrowser.Query(new([a]));Equal(2,r.Entries.Count);Equal(true,r.Entries.Any(e=>e.IsDirectory));
    });

    Check("desktop category is applied before the result limit and leaves files in place", () => {
        var folder=Root("classes");
        var picture=Write(folder,"室内.PNG",1);
        Write(folder,"草案.docx",2);Write(folder,"备份.zip",3);Write(folder,"收藏.lnk",4);Root("classes/搬运目标");
        var result=FileBrowser.Query(new([folder],MaxResults:1,Category:DesktopFileCategory.Images));
        Equal(picture,result.Entries.Single().Path);Equal(false,result.Incomplete);Equal(true,File.Exists(picture));
        Equal("搬运目标",FileBrowser.Query(new([folder],Category:DesktopFileCategory.Folders)).Entries.Single().Name);
    });
    Check("overlapping recent roots cannot duplicate files", () => {
        var a=Root("overlap");var b=Root("overlap/子目录");Write(b,"报告.docx",1);
        var r=FileBrowser.Query(new([a,b,a],Recursive:true,FilesOnly:true));Equal(1,r.Entries.Count);
    });
    Check("incomplete downloads and hidden files do not look ready to open", () => {
        var a=Root("partial");foreach(var n in new[]{"a.crdownload","b.part","c.partial","d.tmp","~$报告.docx"})Write(a,n,2);
        var hidden=Write(a,"隐藏.txt",2);File.SetAttributes(hidden,FileAttributes.Hidden);Write(a,"完成.zip",1);
        var r=FileBrowser.Query(new([a]));Equal(1,r.Entries.Count);Equal("完成.zip",r.Entries[0].Name);
    });
    Check("missing root is reported separately from a genuinely empty folder", () => {
        var missing=FileBrowser.Query(new([Path.Combine(scratch,"missing")]));Equal(true,missing.Incomplete);Equal(0,missing.ReadableRoots);
        var empty=FileBrowser.Query(new([Root("empty")]));Equal(false,empty.Incomplete);Equal(1,empty.ReadableRoots);
    });
    Check("scan limit reports partial results without scanning whole directory", () => {
        var a=Root("limit");for(int i=0;i<8;i++)Write(a,$"{i}.txt",i);
        var r=FileBrowser.Query(new([a],MaxScanned:3));Equal(true,r.Incomplete);Equal(3,r.Scanned);Equal(3,r.Entries.Count);
    });
    Check("result limit keeps newest files and reports truncation", () => {
        var a=Root("result-limit");Write(a,"旧.txt",1);Write(a,"中.txt",2);Write(a,"新.txt",3);
        var r=FileBrowser.Query(new([a],MaxResults:2));Equal(true,r.Incomplete);Equal(2,r.Entries.Count);Equal("新.txt",r.Entries[0].Name);
    });
    Check("depth limit reports nested directories excluded from search", () => {
        var a=Root("depth");var sub=Root("depth/sub");Write(sub,"深层.txt",1);
        var r=FileBrowser.Query(new([a],Recursive:true,FilesOnly:true,MaxDepth:0));Equal(true,r.Incomplete);Equal(0,r.Entries.Count);
    });
    Check("cancelled queries throw instead of returning a misleading empty list", () => {
        using var stop=new CancellationTokenSource();stop.Cancel();try {FileBrowser.Query(new([scratch]),stop.Token);throw new Exception("cancellation ignored");}catch(OperationCanceledException){}
    });
    Check("a newly saved file appears in a subsequent query", () => {
        var a=Root("refresh");Equal(0,FileBrowser.Query(new([a])).Entries.Count);Write(a,"刚新建.txt",1);Equal("刚新建.txt",FileBrowser.Query(new([a])).Entries.Single().Name);
    });
    Check("navigation enters children and back/up do not escape source root",()=>{
        var a=Root("navigation");var sub=Root("navigation/子目录");var settings=new FilePanelSettings();
        var nav=new FileNavigation([new("desktop","桌面",a),new("downloads","下载",Root("nav-downloads"))],settings);
        Equal(true,nav.IsRecent);nav.Select("desktop");Equal(a,nav.CurrentDirectory);nav.Enter(sub);Equal(sub,nav.CurrentDirectory);
        nav.Up();Equal(a,nav.CurrentDirectory);Equal(false,nav.CanUp);nav.Back();Equal(sub,nav.CurrentDirectory);
        nav.Enter(Root("outside"));Equal(sub,nav.CurrentDirectory);nav.Select("recent");Equal(true,nav.CreateQuery("").FilesOnly);Equal(2,nav.CreateQuery("报告").Roots.Count);
    });
    Check("an added drive root permits entering its immediate child",()=>{
        var volume=Path.GetPathRoot(scratch)!;var nav=new FileNavigation([new("volume","磁盘",volume)],new FilePanelSettings());
        nav.Select("volume");var child=Directory.GetDirectories(volume).First(p=>Path.GetFileName(p).Equals("Users",StringComparison.OrdinalIgnoreCase));
        nav.Enter(child);Equal(child,nav.CurrentDirectory);nav.Up();Equal(volume,nav.CurrentDirectory);Equal(false,nav.CanUp);
    });
    Check("recent selection persists and legacy settings retain app order",()=>{
        var path=Path.Combine(scratch,"old.json");File.WriteAllText(path,"""{"Items":[{"Id":"a","Name":"原入口","Path":"C:\\Apps\\test.lnk"}],"Width":940}""");
        var s=DockStore.Load(path);Equal("recent",s.Files.SelectedSource);Equal(0,s.Files.AdditionalFolders.Count);Equal("原入口",s.Items.Single().Name);
        s.Files.AdditionalFolders.Add(Root("additional"));s.Files.SelectedSource="downloads";DockStore.Save(path,s);var reload=DockStore.Load(path);Equal("downloads",reload.Files.SelectedSource);Equal(1,reload.Files.AdditionalFolders.Count);
    });
    Check("invalid optional file settings cannot destroy old dock layout",()=>{
        var path=Path.Combine(scratch,"optional.json");File.WriteAllText(path,"""{"Items":[],"Files":{"SelectedSource":null,"AdditionalFolders":[null,"","C:\\Notes","c:\\notes" ]}}""");
        var s=DockStore.Load(path);Equal("recent",s.Files.SelectedSource);Equal(1,s.Files.AdditionalFolders.Count);
        File.WriteAllText(path,"""{"Items":[],"Files":null}""");Equal("recent",DockStore.Load(path).Files.SelectedSource);
    });
} finally { Directory.Delete(scratch,true); }
Console.WriteLine($"Failures: {failures}");return failures==0?0:1;
