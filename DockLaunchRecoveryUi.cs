using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;

namespace FloatingDock;

public sealed partial class DockWindow
{
    Popup launchIssue = null!;
    TextBlock launchIssueTitle = null!, launchIssueEntry = null!;
    Tile? launchIssueTile;
    bool launchPickerOpen;

    void InitializeLaunchRecovery()
    {
        var body = new StackPanel();
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var dismiss = FileAction("×", CloseLaunchIssue, 26);
        dismiss.Margin = new Thickness(6, 0, 0, 0);
        dismiss.Padding = new Thickness(3);
        AutomationProperties.SetName(dismiss, "关闭启动提示");
        DockPanel.SetDock(dismiss, Dock.Right);
        heading.Children.Add(dismiss);
        launchIssueTitle = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = GlassUi.Text,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetLiveSetting(launchIssueTitle, AutomationLiveSetting.Polite);
        heading.Children.Add(launchIssueTitle);
        body.Children.Add(heading);
        launchIssueEntry = new TextBlock
        {
            FontSize = 12, Foreground = GlassUi.Muted, TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 0, 9)
        };
        body.Children.Add(launchIssueEntry);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var retry = FileAction("重试", () =>
        {
            if (launchIssueTile is { } tile) OpenDockItem(tile);
        }, 52);
        actions.Children.Add(retry);
        actions.Children.Add(FileAction("重新选择入口", ChooseLaunchEntry, 94));
        body.Children.Add(actions);
        var card = new Border
        {
            Name = "LaunchIssueCard", Width = 238, Padding = new Thickness(11),
            BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#805D718D"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Background = GlassUi.Panel, Child = body
        };
        card.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            var button = launchIssueTile?.Button;
            CloseLaunchIssue();
            button?.Focus();
            e.Handled = true;
        };
        launchIssue = new Popup
        {
            Child = card, AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Custom,
            PopupAnimation = SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None
        };
        launchIssue.CustomPopupPlacementCallback = (size, target, _) =>
        {
            if (launchIssue.PlacementTarget is not FrameworkElement button) return [];
            var anchor = button.TranslatePoint(new Point(), this);
            var area = NativeServices.WorkingArea(this);
            double x = Left + anchor.X, y = Top + anchor.Y;
            var place = DockLayout.Panel(new(area.Left, area.Top, area.Width, area.Height),
                x + (size.Width - target.Width) / 2, y, target.Width, target.Height, size.Width, size.Height);
            return [new CustomPopupPlacement(new Point(place.Left - x, place.Top - y), PopupPrimaryAxis.None)];
        };
        launchIssue.Closed += (_, _) =>
        {
            if (launchIssue.IsOpen) return;
            launchIssueTile = null;
            launchIssue.PlacementTarget = null;
            if (shortcutRefreshDeferred && !launchPickerOpen) RefreshDesktopEntries();
        };
        launchIssue.Opened += (_, _) => retry.Focus();
        LocationChanged += (_, _) => CloseLaunchIssue();
        Closed += (_, _) => CloseLaunchIssue();
    }

    void ShowLaunchIssue(Tile tile, string title)
    {
        if (!tiles.Contains(tile)) return;
        if (launchIssueTile != tile) CloseLaunchIssue();
        hovering = false;
        nameBubble.Visibility = Visibility.Collapsed;
        DesktopLayer.SetShortcutPresentation(this, true);
        shortcutPresentation.Start();
        launchIssueTile = tile;
        launchIssueTitle.Text = title;
        launchIssueEntry.Text = tile.Item.Name;
        launchIssue.PlacementTarget = tile.Button;
        launchIssue.IsOpen = true;
        UIElementAutomationPeer.FromElement(launchIssueTitle)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    void CloseLaunchIssue() => launchIssue.IsOpen = false;

    void ChooseLaunchEntry()
    {
        if (launchIssueTile is not { } tile) return;
        var lastTitle = launchIssueTitle.Text;
        launchPickerOpen = true;
        CloseLaunchIssue();
        try
        {
            var picker = new OpenFileDialog
            {
                Title = "重新选择入口",
                Filter = "快捷方式和应用|*.lnk;*.url;*.exe;*.appref-ms|所有文件|*.*",
                DereferenceLinks = false
            };
            if (picker.ShowDialog(this) != true)
            {
                ShowLaunchIssue(tile, lastTitle);
                return;
            }
            if (ReplaceLaunchEntry(tile.Item, picker.FileName) is { } error)
                ShowLaunchIssue(tile, error);
        }
        finally
        {
            launchPickerOpen = false;
            if (shortcutRefreshDeferred && !launchIssue.IsOpen) RefreshDesktopEntries();
        }
    }

    // Update the existing item so its display name, ID, group and fixed slot stay together.
    string? ReplaceLaunchEntry(DockItem item, string path)
    {
        if (!state.Items.Contains(item)) return "这个入口已移出 Dock";
        string selected;
        try
        {
            selected = Path.GetFullPath(path);
            ShortcutTarget.Check(selected);
        }
        catch (Exception) { return "所选入口无法打开"; }
        if (state.Items.Any(other => other != item && string.Equals(other.Path, selected, StringComparison.OrdinalIgnoreCase)))
            return "这个入口已在 Dock 中";

        var previous = item.Path;
        if (string.Equals(selected, previous, StringComparison.OrdinalIgnoreCase)) return null;
        var ignored = state.IgnoredDesktopShortcuts;
        state.IgnoredDesktopShortcuts = ignored.Append(previous)
            .Where(value => !string.Equals(value, selected, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        item.Path = selected;
        try { DockStore.Save(statePath, state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            item.Path = previous;
            state.IgnoredDesktopShortcuts = ignored;
            return "入口暂时无法保存";
        }
        Rebuild();
        WatchShortcutSources();
        return null;
    }
}
