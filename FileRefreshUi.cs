using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DockWindow
{
    FileChangeMonitor? fileChanges;
    readonly DispatcherTimer fileChangeDelay=new(){Interval=TimeSpan.FromMilliseconds(120)};
    bool fileChangesPending;

    bool FileInteractionBusy=>menuOpen||fileDraggingOut||fileDragActive||fileTransferBusy||(filePressedPath is not null&&System.Windows.Input.Mouse.LeftButton==System.Windows.Input.MouseButtonState.Pressed)||collectionItems.Children.OfType<System.Windows.Controls.Button>().Any(b=>b.ContextMenu?.IsOpen==true);
    bool CanRefreshFiles()=>collectionPopup.IsOpen&&!fileQueryBusy&&!fileSearchDelay.IsEnabled&&!FileInteractionBusy;
    bool CanPopulateInitialDragView=>fileInitialDragView&&fileDragActive&&collectionItems.Children.Count==0&&!fileDraggingOut&&!fileTransferBusy&&!menuOpen;
    void InitializeFileRefresh()
    {
        fileChangeDelay.Tick+=(_,_)=>{
            if(!collectionPopup.IsOpen){fileChangeDelay.Stop();return;}
            if(!fileChangesPending){fileChangeDelay.Stop();return;}
            if(!CanRefreshFiles())return;
            fileChangeDelay.Stop();fileChangesPending=false;_=RefreshFilesAsync(true);
        };
    }
    void StartFileWatching()
    {
        fileChanges?.Dispose();
        var roots=fileNavigation.IsRecent?fileNavigation.Sources.Select(s=>s.Path):[fileNavigation.CurrentDirectory];
        fileChanges=new(roots.Select(p=>new WatchRoot(p,true)),()=>{
            if(Dispatcher.HasShutdownStarted)return;
            _=Dispatcher.BeginInvoke(DispatcherPriority.Background,()=>{
                if(!collectionPopup.IsOpen)return;fileChangesPending=true;fileChangeDelay.Start();
            });
        });
    }
    void StopFileWatching(){fileChanges?.Dispose();fileChanges=null;fileChangeDelay.Stop();fileChangesPending=false;}
}
