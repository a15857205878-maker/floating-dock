using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FloatingDock;

public sealed partial class DockWindow
{
    FileNavigation fileNavigation=null!;
    TextBlock fileLocation=null!;
    Button fileBack=null!,fileUp=null!;
    ScrollViewer fileScroll=null!;
    readonly Dictionary<string,Button> fileSourceButtons=new();
    readonly DispatcherTimer fileRefresh=new(){Interval=TimeSpan.FromSeconds(15)};
    readonly DispatcherTimer fileSearchDelay=new(){Interval=TimeSpan.FromMilliseconds(250)};
    readonly System.Threading.SemaphoreSlim fileIconSlots=new(4);
    readonly Dictionary<string,(DateTime Stamp,ImageSource Image)> fileIconCache=new(StringComparer.OrdinalIgnoreCase);
    CancellationTokenSource? fileQueryStop,fileIconsStop;
    bool fileQueryBusy,fileChanging;
    string fileResultStamp="";
    int fileIconVersion;

    IEnumerable<FileSource> FileSources()
    {
        yield return new("desktop","桌面收件箱",Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        yield return new("downloads","下载",FileLocations.Downloads());
        yield return new("archive","收纳",CollectionDirectory);
        foreach(var folder in state.Files.AdditionalFolders)
            yield return new("folder:"+folder,Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)) is {Length:>0} name?name:folder,folder);
    }

    Button FileAction(string label,Action action,double width=48)
    {
        var button=new Button{Content=label,Template=GlassUi.ButtonTemplate(),HorizontalContentAlignment=HorizontalAlignment.Center,FontSize=12,MinWidth=width,Padding=new Thickness(7,5,7,5),Margin=new Thickness(0,0,5,0),Background=Brush("#25FFFFFF"),Foreground=Brushes.White,BorderThickness=new Thickness(0),Cursor=Cursors.Hand};
        AutomationProperties.SetName(button,label);button.Click+=(_,_)=>action();return button;
    }

    void InitializeFileBrowser()
    {
        fileNavigation=new(FileSources(),state.Files);
        BuildFilePanel();
        collectionPopup.Opened+=(_,_)=>{
            if(quickDropCompact)return;
            filesButton.Background=Brush("#405F99C7");collectionSurface.Focus();fileRefresh.Start();StartFileWatching();
        };
        collectionPopup.Closed+=(_,_)=>{
            if(collectionPopup.IsOpen)return;
            filesButton.Background=Brush("#01000000");hovering=false;fileRefresh.Stop();fileSearchDelay.Stop();
            StopFileWatching();CancelFileQuery();CancelFileIcons();
            // Closing the previous popup must not cancel a new dwell already started on the Dock.
            if(!fileExpandDelay.IsEnabled)EndFileDrag();
        };
        fileSearchDelay.Tick+=(_,_)=>{fileSearchDelay.Stop();PopulateCollection();};
        fileRefresh.Tick+=(_,_)=>{if(CanRefreshFiles())_=RefreshFilesAsync(true);};
        InitializeFileRefresh();InitializeFileInteraction();
        collectionSurface.PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.Escape){if(quickDropTemporary)EndFileDrag();else CloseCollection();e.Handled=true;}
            else if(e.Key==Key.F&&(Keyboard.Modifiers&ModifierKeys.Control)!=0){collectionSearch.Focus();collectionSearch.SelectAll();e.Handled=true;}
            else if(e.Key==Key.F5){PopulateCollection();e.Handled=true;}
            else if((e.Key==Key.Left||e.SystemKey==Key.Left)&&(Keyboard.Modifiers&ModifierKeys.Alt)!=0){NavigateFiles(fileNavigation.Back);e.Handled=true;}
            else if((e.Key==Key.Up||e.SystemKey==Key.Up)&&(Keyboard.Modifiers&ModifierKeys.Alt)!=0){NavigateFiles(fileNavigation.Up);e.Handled=true;}
        };
        UpdateFileNavigation();
    }

    void UpdateFileNavigation()
    {
        fileBack.IsEnabled=fileNavigation.CanBack;fileUp.IsEnabled=fileNavigation.CanUp;
        UpdatePanelHeader();
    }

    void NavigateFiles(Action action)
    {
        if(fileTransferBusy)return;
        searchApplications=false;todayFilesOnly=false;action();fileChanging=true;collectionSearch.Text="";fileChanging=false;
        ClearFileResults();
        fileScroll.ScrollToTop();UpdateFileNavigation();Save();StartFileWatching();PopulateCollection();
    }

    void ClearFileResults(){CancelFileQuery();CancelFileIcons();fileSelection.Retain([]);collectionItems.Children.Clear();fileResultStamp="";}

    void CancelFileQuery()
    {
        collectionVersion++;fileQueryStop?.Cancel();fileQueryStop?.Dispose();fileQueryStop=null;fileQueryBusy=false;
    }
    void CancelFileIcons(){fileIconVersion++;fileIconsStop?.Cancel();fileIconsStop?.Dispose();fileIconsStop=null;}
    void PopulateCollection(){if(collectionSearch is null||quickDropCompact)return;fileSearchDelay.Stop();UpdateFileNavigation();_=RefreshFilesAsync(false);}

    async Task RefreshFilesAsync(bool quiet)
    {
        if(!collectionPopup.IsOpen)return;
        CancelFileQuery();var stop=new CancellationTokenSource();fileQueryStop=stop;var token=stop.Token;
        int version=collectionVersion;fileQueryBusy=true;
        var query=fileNavigation.CreateQuery(collectionSearch.Text) with {
            CreatedSinceUtc=todayFilesOnly?DateTime.Today.ToUniversalTime():null,
            Category=fileNavigation.SelectedId=="desktop" ? desktopCategory : DesktopFileCategory.All
        };
        bool includeApps=searchApplications;var searchText=collectionSearch.Text;var apps=state.Items.Select(i=>new DockItem{Id=i.Id,Name=i.Name,Path=i.Path,Group=i.Group}).ToArray();
        if(!quiet)collectionStatus.Text="正在查找文件…";
        try
        {
            var result=await Task.Run(()=>{var result=FileBrowser.Query(query,token);if(includeApps){token.ThrowIfCancellationRequested();result=FileSearch.Apply(apps.Where(i=>File.Exists(i.Path)||Directory.Exists(i.Path)),result,searchText,query.CreatedSinceUtc);}return result;},token);
            if(token.IsCancellationRequested||version!=collectionVersion||!collectionPopup.IsOpen)return;
            if(FileInteractionBusy&&!CanPopulateInitialDragView){fileChangesPending=true;fileChangeDelay.Start();return;}
            collectionStatus.Text=result.ReadableRoots==0&&result.Entries.Count==0?"目录暂不可用，可从菜单重新选择或在资源管理器查看。":$"{result.Entries.Count} 个项目 · 单击选择，双击打开 · 可拖入/拖出";
            if(result.Incomplete)collectionStatus.Text+="\n结果有范围限制或部分目录不可读；最多显示200项、扫描6层，可细化关键词或进入子目录查找。";
            else if(result.Entries.Count==0)collectionStatus.Text=string.IsNullOrWhiteSpace(query.Search)?"这里暂时没有文件；新保存的文件会自动刷新。":"没有匹配的文件，可更换入口或关键词。";
            todayFilter.Content="今天新增  "+result.Entries.Count(e=>e.CreatedUtc>=DateTime.Today.ToUniversalTime())+(result.Incomplete?"+":"");
            if (fileNavigation.SelectedId=="desktop")
                collectionStatus.Text += " · 可拖到左侧目录";
            fileStatus=collectionStatus.Text;
            var stamp=includeApps.ToString()+todayFilesOnly.ToString()+fileNavigation.SelectedId+fileNavigation.CurrentDirectory+query.Search+query.Category+string.Join("|",result.Entries.Select(e=>$"{e.Path}:{e.ModifiedUtc.Ticks}:{e.Size}:{e.IsDirectory}"));
            if(stamp!=fileResultStamp||collectionItems.Children.Count==0)
            {
                fileResultStamp=stamp;RenderFileResults(result.Entries);
            }
            UpdateFileSelection();
        }
        catch(OperationCanceledException){}
        catch(Exception ex)
        {
            if(version==collectionVersion&&collectionPopup.IsOpen)collectionStatus.Text="文件暂时无法读取，请刷新或在资源管理器中查看。";
            LifecycleLog.Write("file-query-failed "+ex.GetType().Name);
        }
        finally {if(version==collectionVersion)fileQueryBusy=false;}
    }

    void RenderFileResults(IReadOnlyList<BrowserEntry> entries)
    {
        var focused=(Keyboard.FocusedElement as Button)?.Tag as BrowserEntry;
        double offset=fileScroll.VerticalOffset;
        fileSelection.Retain(entries.Select(e=>e.Path));
        CancelFileIcons();fileIconsStop=new();var token=fileIconsStop.Token;int version=fileIconVersion;
        collectionItems.Children.Clear();
        foreach(var entry in entries)
        {
            var image=new Image{Source=SystemArtwork.For("文件")};
            var body=FileRowContent(entry,image);
            var button=new Button{Tag=entry,Content=body,HorizontalContentAlignment=HorizontalAlignment.Stretch,MinHeight=fileNavigation.IsRecent||!string.IsNullOrWhiteSpace(collectionSearch.Text)?54:44,Template=GlassUi.ButtonTemplate(),Margin=new Thickness(0,0,0,2),Background=Brush("#06FFFFFF"),BorderThickness=new Thickness(0),Cursor=Cursors.Hand,ToolTip=entry.Path,ContextMenu=FileEntryMenu(entry)};
            AutomationProperties.SetName(button,entry.Name);
            ConfigureFileRow(button,entry);collectionItems.Children.Add(button);
            if(focused?.Path==entry.Path)button.Focus();
            _=LoadFileIcon(entry,image,version,token);
        }
        fileScroll.ScrollToVerticalOffset(offset);
    }

    void OpenFileEntry(BrowserEntry entry)
    {
        if(entry.IsDirectory&&entry.Kind!="应用"){NavigateFiles(()=>fileNavigation.Enter(entry.Path));return;}
        CloseCollection();Try(()=>{
            if(!File.Exists(entry.Path)&&!Directory.Exists(entry.Path))throw new FileNotFoundException("文件已移动或删除，请刷新文件列表。",entry.Path);
            LaunchFromDock(() => NativeServices.Open(entry.Path));
        });
    }
    ContextMenu FileEntryMenu(BrowserEntry entry)
    {
        var context=new ContextMenu();
        context.Items.Add(Entry(entry.IsDirectory?"进入文件夹":"打开",()=>OpenFileEntry(entry)));
        context.Items.Add(Entry("打开所在位置",()=>{CloseCollection();Try(()=>NativeServices.Reveal(entry.Path));}));
        if(entry.IsDirectory)context.Items.Add(Entry("在资源管理器打开",()=>{CloseCollection();Try(()=>NativeServices.Open(entry.Path));}));
        context.Items.Add(Entry("复制路径",()=>Try(()=>Clipboard.SetText(entry.Path))));
        GlassUi.Menu(context);return context;
    }

    ContextMenu FileBrowserMenu()
    {
        var context=new ContextMenu();
        context.Items.Add(Entry("添加常用文件夹…",AddFileFolder));
        foreach(var source in fileNavigation.Sources.Where(s=>s.Id.StartsWith("folder:",StringComparison.Ordinal)))
            context.Items.Add(Entry("常用："+source.Name,()=>NavigateFiles(()=>fileNavigation.Select(source.Id))));
        if(fileNavigation.SelectedId.StartsWith("folder:",StringComparison.Ordinal))
            context.Items.Add(Entry("移除此常用入口（保留文件）",()=>{
                state.Files.AdditionalFolders.RemoveAll(p=>p.Equals(fileNavigation.Source?.Path,StringComparison.OrdinalIgnoreCase));state.Files.SelectedSource="recent";
                fileNavigation=new(FileSources(),state.Files);NavigateFiles(()=>{});
            }));
        var open=Entry("在资源管理器打开当前文件夹",()=>{CloseCollection();Try(()=>NativeServices.Open(fileNavigation.CurrentDirectory));});open.IsEnabled=!fileNavigation.IsRecent;context.Items.Add(open);
        context.Items.Add(Entry("刷新",PopulateCollection));context.Items.Add(Entry("收起",CloseCollection));return context;
    }

    void AddFileFolder()
    {
        CloseCollection();var dialog=new OpenFolderDialog{Title="添加常用文件夹（可选择浏览器实际下载目录）",Multiselect=false};
        if(dialog.ShowDialog(this)!=true)return;
        var folder=Path.TrimEndingDirectorySeparator(Path.GetFullPath(dialog.FolderName));
        var known=fileNavigation.Sources.FirstOrDefault(s=>s.Path.Equals(folder,StringComparison.OrdinalIgnoreCase));
        if(known is not null){ShowFilePanel(known.Id,false,false);Save();return;}
        if(state.Files.AdditionalFolders.Count>=32){Try(()=>throw new InvalidOperationException("最多添加32个常用文件夹，请先移除不再使用的入口。"));return;}
        state.Files.AdditionalFolders.Add(folder);state.Files.SelectedSource="folder:"+folder;fileNavigation=new(FileSources(),state.Files);ShowFilePanel("folder:"+folder,false,false);Save();
    }

    async Task LoadFileIcon(BrowserEntry entry,Image image,int version,CancellationToken token)
    {
        if(fileIconCache.TryGetValue(entry.Path,out var cached)&&cached.Stamp==entry.ModifiedUtc){image.Source=cached.Image;return;}
        bool acquired=false;
        try
        {
            await fileIconSlots.WaitAsync(token);acquired=true;
            var source=await Task.Run(()=>NativeServices.IconFor(entry.Path),token);
            if(source is null||token.IsCancellationRequested||version!=fileIconVersion||Dispatcher.HasShutdownStarted)return;
            if(fileIconCache.Count>=512)fileIconCache.Clear();fileIconCache[entry.Path]=(entry.ModifiedUtc,source);image.Source=source;
        }
        catch(OperationCanceledException){}
        catch(Exception ex){LifecycleLog.Write("file-icon-failed "+ex.GetType().Name);}
        finally{if(acquired)fileIconSlots.Release();}
    }
}
