using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FloatingDock;

public sealed partial class DockWindow
{
    readonly FileSelection fileSelection=new();
    bool fileDraggingOut,fileDragActive,fileTransferBusy,fileSuppressClick;
    bool filePressedSelected;
    bool fileInitialDragView;
    string? filePressedPath;
    Point filePressedPoint;
    string fileStatus="";
    ShellFileDrop? fileDrop;
    string? fileDropDirectory;
    readonly DispatcherTimer fileExpandDelay=new(){Interval=TimeSpan.FromMilliseconds(550)};
    readonly DispatcherTimer fileLeaveDelay=new(){Interval=TimeSpan.FromMilliseconds(90)};
    string? fileHoverDirectory;

    string[] VisibleFilePaths()=>collectionItems.Children.OfType<Button>().Select(b=>((BrowserEntry)b.Tag).Path).ToArray();
    static T? FindParent<T>(DependencyObject? child,Func<T,bool>? matches=null) where T:DependencyObject
    {
        while(child is not null)
        {
            if(child is T found&&(matches is null||matches(found)))return found;
            child=child is Visual||child is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(child):(child as FrameworkElement)?.Parent;
        }
        return null;
    }
    bool IsFileSurface(DependencyObject? source)=>FindParent<Border>(source,b=>ReferenceEquals(b,collectionSurface)) is not null;
    bool IsFilesButton(DependencyObject? source)=>FindParent<Button>(source,b=>ReferenceEquals(b,filesButton)) is not null;

    bool IsOrganizerButton(DependencyObject? source)=>FindParent<Button>(source,b=>ReferenceEquals(b,organizeButton)) is not null;

    void InitializeFileInteraction()
    {
        collectionSurface.AllowDrop=true;
        collectionSurface.PreviewDragOver+=HandleFileDragOver;collectionSurface.PreviewDrop+=HandleFileDrop;
        collectionSurface.DragLeave+=(_,_)=>ScheduleFileDragLeave();
        fileLeaveDelay.Tick+=(_,_)=>EndFileDrag();
        fileExpandDelay.Tick+=(_,_)=>{
            fileExpandDelay.Stop();
            bool organizer=fileHoverDirectory=="@desktop";
            if(organizer && state.Files.QuickDropCollapsed)
            {
                fileDragActive=true;collectionPopup.StaysOpen=true;
                SetOrganizerDragCue(true);
                ShowTemporaryQuickDrop();
                return;
            }
            if(!collectionPopup.IsOpen && (organizer||fileHoverDirectory=="@files"))
            {
                fileDragActive=true;collectionPopup.StaysOpen=true;
                if(organizer)SetOrganizerDragCue(true);
                fileInitialDragView=true;
                if(organizer) OpenDesktopOrganizer();
                else ToggleCollection();
            }
        };
        collectionSurface.PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.A&&(Keyboard.Modifiers&ModifierKeys.Control)!=0&&Keyboard.FocusedElement is not TextBox){fileSelection.SelectAll(VisibleFilePaths());UpdateFileSelection();e.Handled=true;}
        };
        Closing+=(_,e)=>{if(fileTransferBusy||fileDraggingOut){e.Cancel=true;collectionStatus.Text="正在拖放或处理文件，请先完成或取消操作后再退出。";}};
    }

    void ConfigureFileRow(Button button,BrowserEntry entry)
    {
        button.PreviewMouseLeftButtonDown+=(_,e)=>{
            filePressedPath=entry.Path;filePressedPoint=e.GetPosition(button);
            filePressedSelected=fileSelection.Contains(entry.Path);
            var modifiers=Keyboard.Modifiers;
            fileSelection.Select(entry.Path,VisibleFilePaths(),modifiers.HasFlag(ModifierKeys.Control),modifiers.HasFlag(ModifierKeys.Shift),preserveExisting:true);UpdateFileSelection();
        };
        button.Click+=(_,_)=>{
            if(fileSuppressClick||fileTransferBusy||!VisibleFilePaths().Contains(entry.Path,StringComparer.OrdinalIgnoreCase))return;
            if(filePressedPath is null||Keyboard.Modifiers==ModifierKeys.None||(filePressedSelected&&Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))fileSelection.Select(entry.Path,VisibleFilePaths(),Keyboard.Modifiers.HasFlag(ModifierKeys.Control),Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            filePressedPath=null;UpdateFileSelection();
        };
        button.MouseDoubleClick+=(_,e)=>{if(e.ChangedButton==MouseButton.Left){filePressedPath=null;OpenFileEntry(entry);e.Handled=true;}};
        button.PreviewKeyDown+=(_,e)=>{
            if(e.Key==Key.Enter){OpenFileEntry(entry);e.Handled=true;}
            else if(e.Key is Key.Up or Key.Down){
                var rows=collectionItems.Children.OfType<Button>().ToArray();int index=Array.IndexOf(rows,button);int next=Math.Clamp(index+(e.Key==Key.Up?-1:1),0,rows.Length-1);
                rows[next].Focus();var path=((BrowserEntry)rows[next].Tag).Path;fileSelection.Select(path,VisibleFilePaths(),Keyboard.Modifiers.HasFlag(ModifierKeys.Control),Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));UpdateFileSelection();e.Handled=true;
            }
        };
        button.PreviewMouseRightButtonDown+=(_,_)=>{if(!fileSelection.Contains(entry.Path)){fileSelection.Select(entry.Path,VisibleFilePaths(),false,false);UpdateFileSelection();}};
        button.PreviewMouseMove+=(_,e)=>{
            if(filePressedPath!=entry.Path||e.LeftButton!=MouseButtonState.Pressed||fileDraggingOut)return;
            var p=e.GetPosition(button);
            if(Math.Abs(p.X-filePressedPoint.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-filePressedPoint.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            filePressedPath=null;fileSuppressClick=true;fileDraggingOut=true;collectionPopup.StaysOpen=true;
            try
            {
                var paths=VisibleFilePaths().Where(fileSelection.Contains).Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
                if(paths.Length>0){using var data=ShellDragData.ForFiles(paths);DragDrop.DoDragDrop(button,data.Data,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link);}
            }
            catch(Exception ex){Try(()=>throw new IOException("无法拖出所选文件："+ex.Message,ex));}
            finally
            {
                fileDraggingOut=false;EndFileDrag();collectionPopup.StaysOpen=false;
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,()=>fileSuppressClick=false);
                if(!IsActive)CloseCollection();else if(collectionPopup.IsOpen)PopulateCollection();
            }
        };
    }

    void UpdateFileSelection()
    {
        foreach(var row in collectionItems.Children.OfType<Button>())row.Background=Brush(fileSelection.Contains(((BrowserEntry)row.Tag).Path)?"#505F99C7":"#06FFFFFF");
        UpdateRevealFileButton();
        if(fileStatus.Length>0)collectionStatus.Text=fileStatus+(fileSelection.Count>0?$" · 已选择{fileSelection.Count}项":"");
    }

    string? FileTargetDirectory(DragEventArgs e)
    {
        var button=FindParent<Button>(e.OriginalSource as DependencyObject);
        return button?.Tag switch
        {
            FileSource source => source.Path,
            BrowserEntry {IsDirectory:true} entry => entry.Path,
            _ when button is not null && fileSourceButtons.TryGetValue("recent", out var recent) && button == recent => null,
            _ => fileNavigation.IsRecent ? null : fileNavigation.CurrentDirectory
        };
    }
    void EndFileDrag()
    {
        fileLeaveDelay.Stop();fileExpandDelay.Stop();fileHoverDirectory=null;fileDrop?.Dispose();fileDrop=null;fileDropDirectory=null;fileDragActive=false;fileInitialDragView=false;
        SetOrganizerDragCue(false);
        ShowQuickDropFeedback(null,DragDropEffects.None);
        RestoreQuickDropAfterDrag();
        if(collectionPopup is not null&&!fileDraggingOut&&!fileTransferBusy)collectionPopup.StaysOpen=false;
        UpdateFileSelection();
    }
    void ScheduleFileDragLeave(bool restart = true)
    {
        if(fileExpandDelay.IsEnabled)
        {
            fileExpandDelay.Stop();
            fileHoverDirectory=null;
            SetOrganizerDragCue(false);
        }
        if(restart) fileLeaveDelay.Stop();
        var grace=TimeSpan.FromMilliseconds(quickDropTemporary ? 340 : 90);
        if(fileLeaveDelay.Interval!=grace)fileLeaveDelay.Interval=grace;
        fileLeaveDelay.Start();
    }

    void ExpandFileButtonForDrag(bool desktop)
    {
        fileLeaveDelay.Stop();
        if(fileTransferBusy||fileDraggingOut||quickDropTemporary)return;
        if(collectionPopup.IsOpen&&(!desktop||!state.Files.QuickDropCollapsed))return;
        var target=desktop?"@desktop":"@files";
        if(fileHoverDirectory==target)return;
        fileHoverDirectory=target;
        fileExpandDelay.Stop();
        fileExpandDelay.Interval=TimeSpan.FromMilliseconds(desktop ? 470 : 550);
        SetOrganizerDragCue(desktop, waiting: desktop);
        fileExpandDelay.Start();
    }

    static uint FileDragKeys(DragDropKeyStates keys)
    {
        uint value=(uint)keys;
        // OLE's Shell targets use Ctrl+Shift for link feedback; WPF also exposes Alt.
        return (value&32)!=0?(value&~32u)|12u:value;
    }

    void HandleFileDragOver(object sender,DragEventArgs e)
    {
        fileLeaveDelay.Stop();
        fileInitialDragView=false;
        if(!e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DragFormat)){e.Effects=DragDropEffects.None;e.Handled=true;return;}
        fileDragActive=true;collectionPopup.StaysOpen=true;
        var source=e.OriginalSource as DependencyObject;
        var button=QuickDropButton(source);
        var target=button?.Tag as QuickDropTarget;
        if (target is null && (quickDropCompact || IsQuickDropSurface(source)))
        {
            e.Effects=DragDropEffects.None;
            e.Handled=true;
            ShowQuickDropFeedback(null,e.Effects);
            return;
        }
        string? directory=target is null ? FileTargetDirectory(e) : target.Path;
        try
        {
            if(directory is null)
            {
                fileDrop?.Dispose();fileDrop=null;fileDropDirectory=null;
                e.Effects=target is not null ? DragDropEffects.None
                    : (e.AllowedEffects&DragDropEffects.Move)!=0 ? DragDropEffects.Move : e.AllowedEffects&DragDropEffects.Copy;
                if(target is null) collectionStatus.Text="释放后选择目标文件夹（最近文件是聚合视图）";
            }
            else
            {
                uint keys=FileDragKeys(e.KeyStates);
                if(fileDropDirectory!=directory||fileDrop is null)
                {
                    fileDrop?.Dispose();fileDrop=null;fileDropDirectory=null;
                    if (target is not null && !Directory.Exists(directory))
                    {
                        e.Effects=DragDropEffects.None;
                        e.Handled=true;
                        ShowQuickDropFeedback(button,e.Effects);
                        return;
                    }
                    fileDrop=new(directory,e.Data,e.AllowedEffects,keys);fileDropDirectory=directory;
                }
                e.Effects=fileDrop.Over(keys);
                if(target is null)
                {
                    string action=e.Effects switch{DragDropEffects.Move=>"移动到",DragDropEffects.Copy=>"复制到",DragDropEffects.Link=>"创建快捷方式到",_=>"无法放入"};
                    collectionStatus.Text=action+"："+directory;
                }
            }
        }
        catch(Exception ex){e.Effects=DragDropEffects.None;LifecycleLog.Write("file-drag-preview-failed "+ex.GetType().Name);}
        ShowQuickDropFeedback(button,e.Effects);
        e.Handled=true;
    }

    void HandleFileDrop(object sender,DragEventArgs e)
    {
        e.Handled=true;e.Effects=DragDropEffects.None;
        if(fileTransferBusy||!e.Data.GetDataPresent(DataFormats.FileDrop)||e.Data.GetDataPresent(DragFormat))return;
        var source=e.OriginalSource as DependencyObject;
        var button=QuickDropButton(source);
        var target=button?.Tag as QuickDropTarget;
        if ((target is null && (quickDropCompact || IsQuickDropSurface(source))) ||
            (target is not null && (button!=quickDropActiveButton || fileDrop is null ||
                fileDrop.Effect==DragDropEffects.None || fileDropDirectory!=target.Path || !Directory.Exists(target.Path))))
        {
            if(fileDragActive) EndFileDrag();
            return;
        }
        var directory=target is null ? FileTargetDirectory(e) : target.Path;
        var data=e.Data;uint keys=FileDragKeys(e.KeyStates);var allowed=e.AllowedEffects;
        try
        {
            fileTransferBusy=true;
            if(directory is null)
            {
                collectionPopup.IsOpen=false;
                var picker=new OpenFolderDialog{Title="选择接收文件的目标文件夹",Multiselect=false};
                if(picker.ShowDialog(this)!=true)return;directory=picker.FolderName;
            }
            if(fileDrop is null||fileDropDirectory!=directory)
            {
                fileDrop?.Dispose();fileDrop=new(directory,data,allowed,keys|1u);fileDropDirectory=directory;
            }
            e.Effects=fileDrop.Drop(keys);
        }
        catch(Exception ex){Try(()=>throw new IOException("Windows未能完成文件操作："+ex.Message,ex));}
        finally
        {
            fileTransferBusy=false;EndFileDrag();
            if(collectionPopup.IsOpen)PopulateCollection();
            RefreshDesktopEntries();RefreshIcons();
        }
    }
}
