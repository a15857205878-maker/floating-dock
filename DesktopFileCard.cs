using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DesktopFileCard:Window
{
    readonly bool downloads;
    readonly Func<string> directory;
    readonly Func<IReadOnlyList<string>> folders;
    readonly ListBox list=new(){Name="CardFiles",SelectionMode=SelectionMode.Extended,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Foreground=GlassUi.Text};
    readonly TextBlock status=new(){Foreground=GlassUi.Muted,FontSize=10,Margin=new Thickness(12,3,12,8)};
    readonly DockPanel body=new();
    readonly Button collapse;
    readonly DispatcherTimer changes=new(){Interval=TimeSpan.FromMilliseconds(180)};
    readonly DispatcherTimer fallback=new(){Interval=TimeSpan.FromSeconds(10)};
    FileChangeMonitor? watcher;
    CancellationTokenSource? queryStop;
    ShellFileDrop? drop;
    string? dropPath;
    bool closed,busy,pending,querying,pressed,shutdown;
    string renderStamp="";
    Point pressPoint;
    ListBoxItem? deferredClick;
    string watchSignature="";
    public DesktopFileCard(string title,bool downloads,Func<string> directory,Func<IReadOnlyList<string>> folders,Action browse,Action? add,Action toggle,Action hide)
    {
        this.downloads=downloads;this.directory=directory;this.folders=folders;
        Title=title;Width=304;Height=250;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=false;
        FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=13;SourceInitialized+=(_,_)=>DesktopLayer.Attach(this);
        var glass=new Grid();glass.Children.Add(new AcrylicMaterial(this,true,true));
        var shell=new DockPanel();var header=new DockPanel{Height=48,Margin=new Thickness(20,0,12,0)};
        var close=ActionButton("×",hide,"隐藏"+title);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        collapse=ActionButton("⌃",toggle,"折叠或展开"+title);DockPanel.SetDock(collapse,Dock.Right);header.Children.Add(collapse);
        header.Children.Add(MovableTitle(title));DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
        var footer=new DockPanel{Margin=new Thickness(8,0,8,5)};
        var open=ActionButton(downloads?"全部下载 ↗":"文件面板 ↗",browse,"浏览"+title);open.Width=106;DockPanel.SetDock(open,Dock.Left);footer.Children.Add(open);
        if(add is not null){var plus=ActionButton("＋ 添加",add,"添加卡片常用文件夹");plus.Width=72;DockPanel.SetDock(plus,Dock.Right);footer.Children.Add(plus);}
        DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);DockPanel.SetDock(status,Dock.Bottom);body.Children.Add(status);
        ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);ScrollViewer.SetVerticalScrollBarVisibility(list,ScrollBarVisibility.Auto);
        list.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=GlassUi.ScrollBarStyle();body.Children.Add(list);shell.Children.Add(body);glass.Children.Add(shell);
        if(!downloads){var panel=new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.UniformGrid));panel.SetValue(System.Windows.Controls.Primitives.UniformGrid.ColumnsProperty,2);list.ItemsPanel=new ItemsPanelTemplate(panel);}
        list.Margin=new Thickness(12,0,12,0);list.Padding=new Thickness(0);
        var cardBorder=new Border{CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(0),Background=SystemParameters.HighContrast?SystemColors.WindowBrush:GlassUi.Color("#30132B3D"),Child=glass,ClipToBounds=true};
        Content=ResizableCard(cardBorder);
        list.MouseDoubleClick+=(_,e)=>{if(Row(e.OriginalSource as DependencyObject) is not null)OpenSelected();};
        list.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){OpenSelected();e.Handled=true;}};
        list.PreviewMouseLeftButtonDown+=(_,e)=>{
            var row=Row(e.OriginalSource as DependencyObject);pressed=row is not null;pressPoint=e.GetPosition(list);deferredClick=null;
            if(row?.IsSelected==true&&list.SelectedItems.Count>1&&Keyboard.Modifiers==ModifierKeys.None){deferredClick=row;e.Handled=true;}
        };
        list.PreviewMouseLeftButtonUp+=(_,e)=>{
            if(pressed&&deferredClick is not null){list.UnselectAll();deferredClick.IsSelected=true;e.Handled=true;}
            deferredClick=null;pressed=false;
        };
        list.PreviewMouseMove+=(_,e)=>{
            var p=e.GetPosition(list);
            if(!pressed||e.LeftButton!=MouseButtonState.Pressed||busy||(Math.Abs(p.X-pressPoint.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-pressPoint.Y)<SystemParameters.MinimumVerticalDragDistance))return;
            pressed=false;deferredClick=null;busy=true;
            try{var selected=list.SelectedItems.OfType<ListBoxItem>().Select(i=>((BrowserEntry)i.Tag).Path).ToArray();if(selected.Length>0){using var data=ShellDragData.ForFiles(selected);DragDrop.DoDragDrop(list,data.Data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link);}}
            catch(Exception ex){Report(ex);}finally{busy=false;Refresh();}
        };
        list.AllowDrop=true;list.DragOver+=DragOverFiles;list.Drop+=DropFiles;list.DragLeave+=(_,_)=>ResetDrop();
        changes.Tick+=(_,_)=>{if(busy||geometryBusy||pressed||list.Items.OfType<ListBoxItem>().Any(i=>i.ContextMenu?.IsOpen==true))return;changes.Stop();Refresh();};
        fallback.Tick+=(_,_)=>Refresh();
        IsVisibleChanged+=(_,_)=>{if(IsVisible){fallback.Start();Refresh();}else{fallback.Stop();changes.Stop();watcher?.Dispose();watcher=null;watchSignature="";queryStop?.Cancel();}};
        Closing+=(_,e)=>{if(!shutdown){e.Cancel=true;hide();}};
        Closed+=(_,_)=>{closed=true;watcher?.Dispose();changes.Stop();fallback.Stop();queryStop?.Cancel();queryStop?.Dispose();ResetDrop();};
    }
    static Button ActionButton(string label,Action action,string name)
    {
        var b=new Button{Content=label,Width=28,Height=28,Foreground=GlassUi.Text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Template=GlassUi.ButtonTemplate(),Cursor=Cursors.Hand,ToolTip=name};
        AutomationProperties.SetName(b,name);b.Click+=(_,_)=>action();return b;
    }
    public void Place(DockArea area,bool collapsed)
    {
        geometryCollapsed=collapsed||area.Height<=48;
        Left=area.Left;Top=area.Top;Width=area.Width;Height=area.Height;body.Visibility=area.Height<=48?Visibility.Collapsed:Visibility.Visible;collapse.Content=collapsed?"⌄":"⌃";
        foreach(var handle in resizeHandles)handle.Visibility=geometryCollapsed&&(handle.Name.Contains("Top")||handle.Name.Contains("Bottom"))?Visibility.Collapsed:Visibility.Visible;
    }
    public void CloseForShutdown(){shutdown=true;Close();}
    public void Refresh()
    {
        if(closed||Dispatcher.HasShutdownStarted)return;
        // Show() may fire before Application.Run installs the WPF synchronization context.
        // Enter via the dispatcher so async query completion always returns to this window.
        Dispatcher.InvokeAsync(RefreshOnDispatcher);
    }
    async void RefreshOnDispatcher()
    {
        if(closed||!IsVisible)return;
        if(pressed&&Mouse.LeftButton!=MouseButtonState.Pressed)pressed=false;
        if(busy||geometryBusy||pressed||list.Items.OfType<ListBoxItem>().Any(i=>i.ContextMenu?.IsOpen==true)){changes.Start();return;}
        if(querying){pending=true;return;}
        querying=true;queryStop?.Dispose();queryStop=new();var token=queryStop.Token;
        try
        {
            var roots=downloads?new[]{directory()}:folders().ToArray();var signature=string.Join('|',roots);
            if(signature!=watchSignature){watcher?.Dispose();watchSignature=signature;watcher=new(roots.Select(p=>new WatchRoot(p,false)),()=>{if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(()=>{if(IsVisible&&!closed)changes.Start();});});}
            BrowserEntry[] entries;FileQueryResult? result=null;
            if(downloads){result=await Task.Run(()=>FileBrowser.Query(new(roots,FilesOnly:true,MaxResults:6),token),token);entries=result.Entries.ToArray();}
            else entries=roots.Select(p=>new BrowserEntry(FolderName(p),p,true,DateTime.MinValue,0)).ToArray();
            if(closed||!IsVisible||token.IsCancellationRequested)return;
            if(busy||geometryBusy||pressed||list.Items.OfType<ListBoxItem>().Any(i=>i.ContextMenu?.IsOpen==true)){changes.Start();return;}
            status.Text=downloads?(result!.ReadableRoots==0?"下载目录暂不可读":entries.Length==0?"暂无已完成下载":result.Incomplete?"最近 6 项 · 更多见全部下载":"最近下载 · 双击打开"):$"{entries.Length} 个位置 · 双击打开";
            var stamp=string.Join('|',entries.Select(e=>$"{e.Path}:{e.ModifiedUtc.Ticks}:{e.Size}"));
            if(renderStamp==stamp&&list.Items.Count==entries.Length)return;
            renderStamp=stamp;
            var selected=list.SelectedItems.OfType<ListBoxItem>().Select(i=>((BrowserEntry)i.Tag).Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var focused=(Keyboard.FocusedElement as ListBoxItem)?.Tag as BrowserEntry;
            list.Items.Clear();
            foreach(var entry in entries)
            {
                var label=new DockPanel();label.Children.Add(new TextBlock{Text=entry.IsDirectory?"\uE8B7":"\uE8A5",FontFamily=new FontFamily("Segoe Fluent Icons"),Foreground=entry.IsDirectory?GlassUi.Color("#F1D69A"):GlassUi.Text,FontSize=19,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});
                if(downloads){var modified=new TextBlock{Text=entry.ModifiedUtc.ToLocalTime().ToString("MM/dd"),FontSize=10,Foreground=GlassUi.Muted,Opacity=.8,Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(modified,Dock.Right);label.Children.Add(modified);}
                label.Children.Add(new TextBlock{Text=entry.Name,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});
                var row=new ListBoxItem{Content=label,Tag=entry,MinHeight=downloads?38:36,Padding=new Thickness(7,7,7,7),HorizontalContentAlignment=HorizontalAlignment.Stretch,ToolTip=entry.Path,IsSelected=selected.Contains(entry.Path)};
                AutomationProperties.SetName(row,entry.Name);row.ContextMenu=RowMenu(entry);list.Items.Add(row);if(focused?.Path==entry.Path)row.Focus();
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!closed)status.Text="暂时无法读取，请从文件面板查看";LifecycleLog.Write("desktop-card-refresh "+ex.GetType().Name);}
        finally{querying=false;if(pending){pending=false;if(!closed&&IsVisible)changes.Start();}}
    }
    static string FolderName(string path)
    {
        if(string.Equals(path,Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),StringComparison.OrdinalIgnoreCase))return "文档";
        if(string.Equals(path,Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),StringComparison.OrdinalIgnoreCase))return "图片";
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
    }
    ContextMenu RowMenu(BrowserEntry entry)
    {
        var menu=new ContextMenu();
        foreach(var action in new[]{("打开",(Action)(()=>NativeServices.Open(entry.Path))),("打开所在位置",()=>NativeServices.Reveal(entry.Path)),("复制路径",()=>Clipboard.SetText(entry.Path))})
        {var item=new MenuItem{Header=action.Item1};item.Click+=(_,_)=>{try{action.Item2();}catch(Exception ex){Report(ex);}};menu.Items.Add(item);}
        GlassUi.Menu(menu);return menu;
    }
    void OpenSelected(){foreach(var entry in list.SelectedItems.OfType<ListBoxItem>().Select(i=>(BrowserEntry)i.Tag).ToArray())try{NativeServices.Open(entry.Path);}catch(Exception ex){Report(ex);}}
    static ListBoxItem? Row(DependencyObject? node){while(node is not null){if(node is ListBoxItem row)return row;node=node is Visual?VisualTreeHelper.GetParent(node):null;}return null;}
    string? Target(DragEventArgs e)=>downloads?directory():(Row(e.OriginalSource as DependencyObject)?.Tag as BrowserEntry)?.Path;
    static uint Keys(DragDropKeyStates keys){uint value=(uint)keys;return(value&32)!=0?(value&~32u)|12u:value;}
    void DragOverFiles(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;var path=Target(e);
        if(path is null||!Directory.Exists(path)){ResetDrop();return;}
        try{if(drop is null||dropPath!=path){ResetDrop();drop=new(path,e.Data,e.AllowedEffects,Keys(e.KeyStates));dropPath=path;}busy=true;e.Effects=drop.Over(Keys(e.KeyStates));}
        catch{ResetDrop();}
    }
    void DropFiles(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;var path=Target(e);if(path is null)return;
        try{if(drop is null||dropPath!=path){ResetDrop();drop=new(path,e.Data,e.AllowedEffects,Keys(e.KeyStates)|1u);}busy=true;e.Effects=drop.Drop(Keys(e.KeyStates));}
        catch(Exception ex){Report(ex);}finally{ResetDrop();Refresh();}
    }
    void ResetDrop(){drop?.Dispose();drop=null;dropPath=null;busy=false;}
    void Report(Exception ex){status.Text="操作未完成："+ex.Message;}
}

