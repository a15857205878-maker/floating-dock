using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatingDock;

public sealed partial class DockWindow
{
    readonly Button[] systemButtons = new Button[3];
    Button filesButton = null!;
    Popup collectionPopup = null!;
    readonly StackPanel collectionItems = new();
    TextBox collectionSearch = null!;
    TextBlock collectionStatus = null!;
    Border collectionSurface = null!;
    int collectionVersion;
    readonly Border weatherDivider = new() { Width = 1, Height = 28, Background = Brush("#45FFFFFF"), IsHitTestVisible = false };
    readonly Border systemDivider = new() { Width = 1, Height = 28, Background = Brush("#45FFFFFF"), IsHitTestVisible = false };
    readonly Border fileDivider = new() { Width = 1, Height = 28, Background = Brush("#45FFFFFF"), IsHitTestVisible = false };
    static readonly (string Name, string Glyph, string Target)[] SystemEntries = [
        ("此电脑", "\uE7F4", "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"),
        ("回收站", "\uE74D", "shell:::{645FF040-5081-101B-9F08-00AA002F954E}"),
        ("控制面板", "\uE713", "shell:::{26EE0668-A00A-44D7-9371-BEB064C98683}")];

    void InitializeCollection()
    {
        for (int i = 0; i < SystemEntries.Length; i++)
        {
            var entry = SystemEntries[i];
            var button = Control(entry.Glyph, entry.Name, 36);
            button.Name = "SystemShortcut" + i;
            button.FontFamily = new FontFamily("Segoe Fluent Icons"); button.FontSize = 24;
            var artwork=SystemArtwork.For(entry.Name);
            if(artwork is not null)button.Content=new Image{Source=artwork,Width=28,Height=28,Stretch=Stretch.Uniform};
            button.Click += (_, _) => OpenSystemEntry(entry.Target);
            root.Children.Add(button); systemButtons[i] = button;
        }
        filesButton = Control("\uE8B7", "文件收纳", 44);
        filesButton.Name="FilesShortcut";
        filesButton.FontFamily = new FontFamily("Segoe Fluent Icons"); filesButton.FontSize = 24;
        var folderArtwork=SystemArtwork.For("文件");
        if(folderArtwork is not null)filesButton.Content=new Image{Source=folderArtwork,Width=28,Height=28,Stretch=Stretch.Uniform};
        filesButton.Click += (_, _) => ToggleCollection(); root.Children.Add(filesButton);
        foreach(var divider in new[]{weatherDivider,systemDivider,fileDivider})root.Children.Add(divider);
        InitializeFileBrowser();
        DesktopLayer.ExternalForeground+=CloseCollection;
        Deactivated += (_, _) => CloseCollection();
        LocationChanged += (_, _) => CloseCollection();
        Closed += (_, _) => {DesktopLayer.ExternalForeground-=CloseCollection;CloseCollection();};
    }

    void OpenSystemEntry(string target) => Try(() => LaunchFromDock(() => DesktopLauncher.Open("explorer.exe", target)));

    string CollectionDirectory => string.IsNullOrWhiteSpace(state.CollectionPath) ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) : state.CollectionPath;

    void ToggleCollection()
    {
        if (collectionPopup.IsOpen) { CloseCollection(); return; }
        var area = NativeServices.WorkingArea(this);
        collectionSurface.Width = Math.Max(240, Math.Min(860, area.Width - 24));
        collectionSurface.MaxHeight = Math.Max(180, Math.Min(640, area.Height - 40));
        ClearFileResults();collectionPopup.IsOpen = true; PopulateCollection();
        hovering = false; nameBubble.Visibility = Visibility.Collapsed;
    }

    void CloseCollection() { if (collectionPopup is not null&&!fileDraggingOut&&!fileDragActive&&!fileTransferBusy) collectionPopup.IsOpen = false; }

}
