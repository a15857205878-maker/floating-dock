using System.IO;
using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DockWindow
{
    FileChangeMonitor? shortcutChanges;
    readonly DispatcherTimer shortcutFallback=new(){Interval=TimeSpan.FromSeconds(15)};
    bool shortcutScanBusy, shortcutRefreshDeferred;
    string shortcutWatchSignature="";
    void StartDesktopSync()
    {
        WatchShortcutSources();RefreshDesktopEntries();
        shortcutFallback.Tick+=(_,_)=>{WatchShortcutSources();RefreshDesktopEntries();};shortcutFallback.Start();
    }
    void StopDesktopSync(){shortcutFallback.Stop();shortcutChanges?.Dispose();shortcutChanges=null;}
    void WatchShortcutSources()
    {
        if(!loaded||iconsClosed)return;
        var paths=NativeServices.DesktopShortcutDirectories().Concat(state.Items.Select(i=>Path.GetDirectoryName(i.Path)??"")).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var signature=string.Join('|',paths.Select(p=>p+":"+Directory.Exists(p)));
        if(signature==shortcutWatchSignature)return;shortcutWatchSignature=signature;
        shortcutChanges?.Dispose();shortcutChanges=new(paths.Select(p=>new WatchRoot(p,false)),()=>{
            if(Dispatcher.HasShutdownStarted)return;
            _=Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{
                if(!loaded||iconsClosed)return;RefreshIcons();RefreshDesktopEntries();WatchShortcutSources();
            });
        });
    }
    async void RefreshDesktopEntries()
    {
        if(!state.AutoImportDesktop||shortcutScanBusy||iconsClosed||internalDrag||pressed is not null)return;
        if(menuOpen||launchIssue.IsOpen||launchPickerOpen){shortcutRefreshDeferred=true;return;}
        shortcutRefreshDeferred=false;
        shortcutScanBusy=true;
        try
        {
            var paths=await Task.Run(()=>NativeServices.DesktopShortcuts().ToArray());
            if(!loaded||iconsClosed||internalDrag||pressed is not null)return;
            if(menuOpen||launchIssue.IsOpen||launchPickerOpen){shortcutRefreshDeferred=true;return;}
            if(DockCatalog.ImportDesktop(state,paths)>0){Rebuild();Save();WatchShortcutSources();}
        }
        catch(Exception ex){LifecycleLog.Write("desktop-sync-failed "+ex.GetType().Name);}
        finally{shortcutScanBusy=false;}
    }
}
