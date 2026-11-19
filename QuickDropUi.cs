using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;

namespace FloatingDock;

public sealed partial class DockWindow
{
    sealed record QuickDropTarget(string Title, string? Path);

    readonly (string Title, Environment.SpecialFolder? Folder, string Glyph)[] quickDropKinds =
    [
        ("文档", Environment.SpecialFolder.MyDocuments, "\uE8A5"),
        ("图片", Environment.SpecialFolder.MyPictures, "\uEB9F"),
        ("项目", null, "\uE8B7")
    ];
    readonly List<Button> quickDropButtons = [];
    readonly StackPanel quickDropPanel = new() { Margin = new Thickness(16, 0, 16, 8), Background = Brushes.Transparent };
    readonly StackPanel quickDropBody = new();
    readonly TextBlock quickDropFeedback = new()
    {
        Name = "QuickDropFeedback", FontSize = 12, Height = 39,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
        Margin = new Thickness(0, 4, 0, 0)
    };
    Button quickDropHeader = null!;
    Button? quickDropActiveButton;
    FrameworkElement[] quickDropFullSections = [];
    bool quickDropTemporary, quickDropCompact;
    (UIElement Target, double Width, double MaxHeight, PopupAnimation Animation) quickDropFullPlacement;
    const string QuickDropPrompt = "将文件拖到对应区块";
    static Brush DropOutline => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#638BAAC7");
    static Brush DropBackground => SystemParameters.HighContrast ? SystemColors.WindowBrush : Brush("#1634597B");

    void AddQuickDropPanel(DockPanel panel)
    {
        quickDropHeader = FileAction("快速归类", ToggleQuickDrop);
        quickDropHeader.HorizontalAlignment = HorizontalAlignment.Left;
        quickDropHeader.Margin = new Thickness(0, 0, 0, 5);
        quickDropPanel.Children.Add(quickDropHeader);
        var layout = new UniformGrid { Columns = quickDropKinds.Length, Rows = 1 };
        foreach (var kind in quickDropKinds)
        {
            var label = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                RenderTransform = new TranslateTransform()
            };
            label.Children.Add(new TextBlock
            {
                Text = kind.Glyph, FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 13,
                Margin = new Thickness(0, 0, 5, 0)
            });
            label.Children.Add(new TextBlock { Text = kind.Title, FontSize = 12 });

            var button = FileAction(kind.Title, () => { }, 0);
            button.Content = label;
            button.MinHeight = 32;
            button.Padding = new Thickness(2, 4, 2, 4);
            button.Margin = new Thickness(0, 0, 5, 0);
            button.Foreground = GlassUi.Text;
            button.Background = DropBackground;
            button.BorderBrush = DropOutline;
            button.BorderThickness = new Thickness(1);
            AutomationProperties.SetName(button, "投放到" + kind.Title);
            button.Click += (_, _) =>
            {
                var target = (QuickDropTarget)button.Tag;
                if (Directory.Exists(target.Path)) Try(() => NativeServices.Open(target.Path!));
                else if (kind.Folder is null) ChooseProjectDropFolder();
            };
            if (kind.Folder is null)
            {
                button.MouseRightButtonUp += (_, args) =>
                {
                    var menu = new ContextMenu();
                    menu.Items.Add(Entry("更换项目文件夹…", ChooseProjectDropFolder));
                    OpenMenu(button, menu);
                    args.Handled = true;
                };
            }
            quickDropButtons.Add(button);
            layout.Children.Add(button);
        }
        quickDropButtons[^1].Margin = new Thickness(0);
        quickDropBody.Children.Add(layout);
        quickDropFeedback.Foreground = GlassUi.Muted;
        quickDropFeedback.Text = QuickDropPrompt;
        AutomationProperties.SetLiveSetting(quickDropFeedback, AutomationLiveSetting.Polite);
        quickDropBody.Children.Add(quickDropFeedback);
        quickDropPanel.Children.Add(quickDropBody);
        quickDropPanel.Visibility = Visibility.Collapsed;
        DockPanel.SetDock(quickDropPanel, Dock.Top);
        panel.Children.Add(quickDropPanel);
    }

    void UpdateQuickDropPanel()
    {
        quickDropPanel.Visibility = fileNavigation.SelectedId == "desktop" || quickDropTemporary ? Visibility.Visible : Visibility.Collapsed;
        if (quickDropPanel.Visibility == Visibility.Collapsed) return;
        bool expanded = quickDropTemporary || !state.Files.QuickDropCollapsed;
        quickDropHeader.Visibility = quickDropCompact ? Visibility.Collapsed : Visibility.Visible;
        quickDropHeader.Content = expanded ? "快速归类  ⌄" : "快速归类  ›";
        AutomationProperties.SetItemStatus(quickDropHeader, expanded ? "已展开" : "已收起");
        quickDropBody.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        for (int i = 0; i < quickDropButtons.Count; i++)
        {
            var kind = quickDropKinds[i];
            var path = kind.Folder is { } folder
                ? Environment.GetFolderPath(folder)
                : state.Files.ProjectDropFolder ?? Path.Combine(CollectionDirectory, "项目");
            var button = quickDropButtons[i];
            button.Tag = new QuickDropTarget(kind.Title, string.IsNullOrWhiteSpace(path) ? null : path);
            button.ToolTip = (string.IsNullOrWhiteSpace(path) ? "目标文件夹不可用" : path)
                + (kind.Folder is null ? "\n右键可更换项目文件夹" : "");
        }
    }

    void ShowTemporaryQuickDrop()
    {
        quickDropTemporary = true;
        if (!collectionPopup.IsOpen)
        {
            quickDropCompact = true;
            var area = NativeServices.WorkingArea(this);
            quickDropFullPlacement = (collectionPopup.PlacementTarget, collectionSurface.Width, collectionSurface.MaxHeight, collectionPopup.PopupAnimation);
            collectionPopup.PopupAnimation = PopupAnimation.None;
            foreach (var section in quickDropFullSections) section.Visibility = Visibility.Collapsed;
            quickDropPanel.Margin = new Thickness(10, 10, 10, 0);
            collectionSurface.Width = Math.Max(1, Math.Min(382, area.Width - 24));
            collectionSurface.MaxHeight = Math.Max(1, area.Height - 24);
            collectionPopup.PlacementTarget = organizeButton;
            UpdateQuickDropPanel();
            collectionPopup.IsOpen = true;
        }
        else UpdateQuickDropPanel();

        if (SystemParameters.ClientAreaAnimation)
            quickDropBody.BeginAnimation(OpacityProperty, new DoubleAnimation(.65, 1, TimeSpan.FromMilliseconds(145))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }

    void RestoreQuickDropAfterDrag()
    {
        if (!quickDropTemporary) return;
        quickDropTemporary = false;
        quickDropBody.BeginAnimation(OpacityProperty, null);
        if (quickDropCompact)
        {
            quickDropCompact = false;
            collectionPopup.IsOpen = false;
            collectionPopup.PlacementTarget = quickDropFullPlacement.Target;
            collectionPopup.PopupAnimation = quickDropFullPlacement.Animation;
            collectionSurface.Width = quickDropFullPlacement.Width;
            collectionSurface.MaxHeight = quickDropFullPlacement.MaxHeight;
            quickDropPanel.Margin = new Thickness(16, 0, 16, 8);
            foreach (var section in quickDropFullSections) section.Visibility = Visibility.Visible;
        }
        UpdateQuickDropPanel();
    }

    void ToggleQuickDrop()
    {
        if (fileDragActive || fileDraggingOut || fileTransferBusy) return;
        state.Files.QuickDropCollapsed = !state.Files.QuickDropCollapsed;
        UpdateQuickDropPanel();
        Save();
    }

    void ChooseProjectDropFolder()
    {
        if (fileDragActive || fileDraggingOut || fileTransferBusy) return;
        var picker = new OpenFolderDialog { Title = "选择项目投放目标文件夹", Multiselect = false };
        if (quickDropButtons[^1].Tag is QuickDropTarget { Path: { } path } && Directory.Exists(path))
            picker.InitialDirectory = path;
        CloseCollection();
        if (picker.ShowDialog(this) == true)
        {
            state.Files.ProjectDropFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(picker.FolderName));
            Save();
        }
        if (!collectionPopup.IsOpen) ToggleCollection();
    }

    static Button? QuickDropButton(DependencyObject? source)
        => FindParent<Button>(source, button => button.Tag is QuickDropTarget && button.IsVisible);

    bool IsQuickDropSurface(DependencyObject? source)
        => FindParent<StackPanel>(source, panel => panel == quickDropPanel) is not null;

    void ShowQuickDropFeedback(Button? button, DragDropEffects effect)
    {
        if (button != quickDropActiveButton)
        {
            if (quickDropActiveButton is { } previous)
            {
                previous.BorderBrush = DropOutline;
                previous.Background = DropBackground;
                previous.Foreground = GlassUi.Text;
                LiftCaption((TranslateTransform)((FrameworkElement)previous.Content).RenderTransform, 0);
                AutomationProperties.SetItemStatus(previous, "");
            }
            if (button is not null) UpdateFileSelection();
            quickDropActiveButton = button;
        }

        string feedback = QuickDropPrompt;
        if (button?.Tag is QuickDropTarget target)
        {
            var operation = effect switch
            {
                DragDropEffects.Move => "移动到",
                DragDropEffects.Copy => "复制到",
                DragDropEffects.Link => "创建快捷方式到",
                _ => "无法投放到"
            };
            feedback = operation + $"「{target.Title}」"
                + (target.Path is { } path ? "\n" + path : "\n目标文件夹不可用");
            if (quickDropFeedback.Text == feedback) return;

            bool available = effect != DragDropEffects.None;
            button.BorderBrush = available
                ? SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brush("#CEC1E3FF")
                : DropOutline;
            button.Background = available
                ? SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brush("#875379A3")
                : DropBackground;
            button.Foreground = available && SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : GlassUi.Text;
            LiftCaption((TranslateTransform)((FrameworkElement)button.Content).RenderTransform, available ? -1.5 : 0);
            AutomationProperties.SetItemStatus(button, operation);
        }
        if (quickDropFeedback.Text == feedback) return;
        quickDropFeedback.Foreground = button is null ? GlassUi.Muted : GlassUi.Text;
        quickDropFeedback.Text = feedback;
        if (UIElementAutomationPeer.FromElement(quickDropFeedback) is { } peer)
            peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}

