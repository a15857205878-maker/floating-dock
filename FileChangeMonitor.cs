using System.IO;

namespace FloatingDock;
public sealed record WatchRoot(string Path,bool Recursive);
public sealed class FileChangeMonitor : IDisposable
{
    readonly List<FileSystemWatcher> watchers=[];
    readonly object gate=new();
    readonly Timer timer;
    readonly Action changed;
    bool disposed;
    long firstChange;

    public FileChangeMonitor(IEnumerable<WatchRoot> roots,Action changed)
    {
        this.changed=changed;timer=new Timer(Notify,null,Timeout.Infinite,Timeout.Infinite);
        foreach(var root in roots.GroupBy(r=>r.Path,StringComparer.OrdinalIgnoreCase).Select(g=>new WatchRoot(g.Key,g.Any(r=>r.Recursive))))
        {
            try
            {
                if(!Directory.Exists(root.Path))continue;
                var watcher=new FileSystemWatcher(root.Path){IncludeSubdirectories=root.Recursive,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite|NotifyFilters.Size,InternalBufferSize=32768};
                watcher.Created+=Changed;watcher.Deleted+=Changed;watcher.Changed+=Changed;watcher.Renamed+=Renamed;watcher.Error+=(_,_)=>Schedule();
                watchers.Add(watcher);watcher.EnableRaisingEvents=true;
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){/* polling remains available */}
        }
    }
    void Changed(object sender,FileSystemEventArgs args)=>Schedule();
    void Renamed(object sender,RenamedEventArgs args)=>Schedule();
    void Schedule()
    {
        lock(gate)
        {
            if(disposed)return;
            long now=Environment.TickCount64;if(firstChange==0)firstChange=now;
            int delay=(int)Math.Clamp(300-(now-firstChange),0,120);timer.Change(delay,Timeout.Infinite);
        }
    }
    void Notify(object? unused)
    {
        lock(gate)
        {
            if(disposed)return;firstChange=0;
            // Serialize callback with Dispose; callbacks only enqueue work, never wait for the UI.
            changed();
        }
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;timer.Dispose();}
        foreach(var watcher in watchers)watcher.Dispose();watchers.Clear();
    }
}
