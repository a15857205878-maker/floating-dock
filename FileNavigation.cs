using System.IO;

namespace FloatingDock;

public sealed record FileSource(string Id,string Name,string Path);
public sealed class FileNavigation
{
    readonly FilePanelSettings settings;
    readonly Stack<(string Id,string Directory)> history=new();
    public IReadOnlyList<FileSource> Sources {get;}
    public string SelectedId {get;private set;}
    public string CurrentDirectory {get;private set;}="";
    public FileSource? Source=>Sources.FirstOrDefault(s=>s.Id==SelectedId);
    public bool IsRecent=>SelectedId=="recent";
    public bool CanBack=>history.Count>0;
    public bool CanUp=>!IsRecent&&!string.Equals(CurrentDirectory,Source?.Path,StringComparison.OrdinalIgnoreCase);

    public FileNavigation(IEnumerable<FileSource> sources,FilePanelSettings settings)
    {
        this.settings=settings;
        Sources=sources.Where(s=>!string.IsNullOrWhiteSpace(s.Path)).Select(s=>s with{Path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(s.Path))}).ToArray();
        SelectedId=Sources.Any(s=>s.Id==settings.SelectedSource)?settings.SelectedSource:"recent";
        CurrentDirectory=Source?.Path??"";
        settings.SelectedSource=SelectedId;
    }
    void Move(string id,string directory)
    {
        if(id==SelectedId&&string.Equals(directory,CurrentDirectory,StringComparison.OrdinalIgnoreCase))return;
        history.Push((SelectedId,CurrentDirectory));SelectedId=id;CurrentDirectory=directory;settings.SelectedSource=id;
    }
    public void Select(string id)
    {
        if(id=="recent")Move(id,"");
        else if(Sources.FirstOrDefault(s=>s.Id==id) is {} source)Move(id,source.Path);
    }
    public void Enter(string path)
    {
        if(Source is not {} source||!Directory.Exists(path))return;
        var full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var prefix=Path.EndsInDirectorySeparator(source.Path)?source.Path:source.Path+Path.DirectorySeparatorChar;
        if(full.Equals(source.Path,StringComparison.OrdinalIgnoreCase)||full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))Move(SelectedId,full);
    }
    public void Up(){if(CanUp&&Directory.GetParent(CurrentDirectory) is {} parent)Enter(parent.FullName);}
    public void Back()
    {
        if(history.TryPop(out var previous)){SelectedId=previous.Id;CurrentDirectory=previous.Directory;settings.SelectedSource=SelectedId;}
    }
    public FileQuery CreateQuery(string search)=>new(IsRecent?Sources.Select(s=>s.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray():[CurrentDirectory],search,Recursive:IsRecent||!string.IsNullOrWhiteSpace(search),FilesOnly:IsRecent,NameSort:settings.NameSort);
}
