using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;

namespace FloatingDock;

public sealed partial class DockWindow
{
    readonly DockLinkedApps? linkedApps;
    readonly DispatcherTimer linkedStatusTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    Dictionary<string, int> linkedRunning = new();
    readonly Dictionary<string, DateTimeOffset> pendingLaunches = new();
    readonly Dictionary<string, DateTimeOffset> lastLinkedClick = new();
    bool linkedPollActive;

    void InitializeLinkedStatus()
    {
        if (linkedApps is null) return;
        linkedStatusTimer.Tick += (_, _) => RefreshLinkedStatus();
        Loaded += (_, _) =>
        {
            RefreshLinkedStatus();
            linkedStatusTimer.Start();
        };
        Closed += (_, _) => linkedStatusTimer.Stop();
    }

    async void RefreshLinkedStatus()
    {
        if (linkedApps is null || linkedPollActive || iconsClosed) return;
        linkedPollActive = true;
        try
        {
            var current = await Task.Run(linkedApps.ReadRunning);
            if (iconsClosed) return;
            linkedRunning = current;
            var now = DateTimeOffset.UtcNow;
            foreach (var (id, deadline) in pendingLaunches.ToArray())
                if (deadline <= now || linkedRunning.ContainsKey(id)) pendingLaunches.Remove(id);
            foreach (var tile in tiles)
                UpdateLinkedTile(tile);
        }
        finally { linkedPollActive = false; }
    }

    void PrepareLinkedTile(Tile tile)
    {
        if (tile.LinkedId is null) return;
        tile.Lamp = new Border
        {
            Name = "AppRunningLamp", Width = 6, Height = 6, CornerRadius = new CornerRadius(3),
            IsHitTestVisible = false, Visibility = Visibility.Hidden
        };
        Panel.SetZIndex(tile.Lamp, 1);
        UpdateLinkedTile(tile);
    }

    void UpdateLinkedTile(Tile tile)
    {
        var id = tile.LinkedId;
        if (id is null || tile.Lamp is null) return;
        bool running = linkedRunning.ContainsKey(id);
        bool launching = !running && pendingLaunches.ContainsKey(id);
        var state = running ? "运行中 · 点击唤回" : launching ? "启动中" : "";
        if (state == tile.LaunchLabel) return;
        tile.LaunchLabel = state;
        tile.Lamp.Background = SystemParameters.HighContrast ? SystemColors.WindowTextBrush
            : GlassUi.Color(running ? "#46A876" : "#5881CA");
        AutomationProperties.SetItemStatus(tile.Button, state);
    }

    void LaunchFromDock(Action launch)
    {
        launch();
        YieldDockToApplication();
    }

    void YieldDockToApplication()
    {
        if (!shortcutPresentation.IsEnabled) return;
        hovering = false;
        nameBubble.Visibility = Visibility.Collapsed;
        EndShortcutPresentation();
    }

    void OpenDockItem(Tile tile)
    {
        try
        {
            if (TryLaunchDockItem(tile)) CloseLaunchIssue();
        }
        catch (Exception ex)
        {
            if (tile.LinkedId is { } id) lastLinkedClick.Remove(id);
            LifecycleLog.Write("dock-launch-failed " + ex.GetType().Name);
            ShowLaunchIssue(tile, ex is FileNotFoundException or DirectoryNotFoundException
                ? "快捷方式已失效" : "暂时无法启动");
        }
    }

    bool TryLaunchDockItem(Tile tile)
    {
        var id = tile.LinkedId;
        if (id is null)
        {
            LaunchFromDock(() => NativeServices.Open(tile.Item.Path));
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        if (lastLinkedClick.TryGetValue(id, out var clicked) && now - clicked < TimeSpan.FromMilliseconds(550)) return false;
        lastLinkedClick[id] = now;

        if (linkedRunning.TryGetValue(id, out int pid))
        {
            DockLinkedApps.AllowForeground(pid);
            if (id != "pet" || DockLinkedApps.SignalPet(pid))
            {
                if (id == "workbench") NativeServices.Open(tile.Item.Path); // Its existing single-instance entry reveals the active WebView.
                YieldDockToApplication();
                LifecycleLog.Write("linked-reveal " + id);
                return true;
            }
        }

        if (pendingLaunches.TryGetValue(id, out var until) && until > now) return false;
        pendingLaunches[id] = now.AddSeconds(10);
        UpdateLinkedTile(tile);
        try { NativeServices.Open(tile.Item.Path); }
        catch
        {
            pendingLaunches.Remove(id);
            UpdateLinkedTile(tile);
            throw;
        }
        YieldDockToApplication();
        RefreshLinkedStatus();
        return true;
    }
}

