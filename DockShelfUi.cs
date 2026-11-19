using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DockWindow
{
    readonly Canvas fixedTiles = new(), scrollingTiles = new();
    readonly Border favoriteDivider = new() { Width = 1, Height = 30, IsHitTestVisible = false, Background = Brush("#60B5DAF1") };
    readonly Border dropMarker = new() { Width = 2, Height = 50, CornerRadius = new CornerRadius(1), IsHitTestVisible = false, Visibility = Visibility.Collapsed, Background = Brush("#FFE6F4FF") };
    int fixedCount;
    double FixedSpan => fixedCount == 0 ? 0 : DockMath.Slot * fixedCount + 6;
    int ScrollingCount => tiles.Count - fixedCount;
    double ScrollingWidth => Math.Max(1, Viewport - FixedSpan);
    Button organizeButton = null!, collapseDockButton = null!;
    Window? dockHandle;
    Button? handleButton;
    Border collapseFace = null!;
    Border? handleFace;
    int dockTransition;
    bool shortcutRecalled;
    readonly DispatcherTimer shortcutPresentation = new() { Interval = TimeSpan.FromSeconds(5) };

    void InitializeDockShelf()
    {
        organizeButton = Control("", "桌面分类与整理", 40);
        organizeButton.ToolTip = "桌面整理\n将文件拖到这里停一下，可展开分类投放";
        AutomationProperties.SetHelpText(organizeButton, "点击查看分类，也可悬停拖入的文件展开投放区");
        InitializeOrganizerCue();
        organizeButton.Click += (_, _) => OpenDesktopOrganizer();
        root.Children.Add(organizeButton);

        collapseDockButton = EdgeControl("收起 Dock", 44, out collapseFace);
        collapseDockButton.Height = 18;
        collapseDockButton.Click += (_, _) => ApplyDockVisibility(false);
        root.Children.Add(collapseDockButton);
        InitializeLaunchRecovery();
        strip.Children.Add(favoriteDivider);
        strip.Children.Add(dropMarker);
        InitializeFavoritePreview();
        shortcutPresentation.Tick += (_, _) =>
        {
            if (root.IsMouseOver || menuOpen || collectionPopup.IsOpen || launchIssue.IsOpen || launchPickerOpen || internalDrag
                || fileDragActive || fileDraggingOut || fileTransferBusy || dockEdgeMotion is not null) return;
            EndShortcutPresentation();
        };

        if (state.DockCollapsed)
        {
            root.Opacity = 0;
            root.IsHitTestVisible = false;
        }
        Closed += (_, _) =>
        {
            ++dockTransition;
            shortcutPresentation.Stop();
            CloseDockEdgeMotion();
            dockHandle?.Close();
        };
    }

    readonly TranslateTransform organizerIconMotion = new();
    readonly ScaleTransform organizerDwellFill = new(0, 1);
    Border organizerDwellLine = null!, organizerDragOutline = null!;
    bool organizerDragCue;

    void InitializeOrganizerCue()
    {
        var face = new Grid { Width = 38, Height = 30, IsHitTestVisible = false };
        organizerDragOutline = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Visibility = Visibility.Hidden
        };
        face.Children.Add(organizerDragOutline);
        var archive = ToolbarStroke(
            "M 1,6 H 5 L 7,8 H 13 L 15,6 H 19 L 17.5,15 H 2.5 Z M 3,3 H 17 M 5,0 H 15");
        archive.Name = "OrganizerIcon";
        archive.RenderTransform = organizerIconMotion;
        face.Children.Add(archive);
        organizerDwellLine = new Border
        {
            Width = 24, Height = 2, Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(1), Visibility = Visibility.Hidden,
            RenderTransform = organizerDwellFill, RenderTransformOrigin = new Point(0, .5)
        };
        face.Children.Add(organizerDwellLine);
        organizeButton.Content = face;
        organizeButton.MouseEnter += (_, _) => RefreshOrganizerLift();
        organizeButton.MouseLeave += (_, _) => RefreshOrganizerLift();
        organizeButton.PreviewMouseLeftButtonDown += (_, _) => LiftCaption(organizerIconMotion, .5);
        organizeButton.PreviewMouseLeftButtonUp += (_, _) => RefreshOrganizerLift();
    }

    void RefreshOrganizerLift()
        => LiftCaption(organizerIconMotion, organizerDragCue || organizeButton.IsMouseOver ? -1.5 : 0);

    void SetOrganizerDragCue(bool active, bool waiting = false)
    {
        if (!organizerDragCue && !active) return;
        organizerDragCue = active;
        organizeButton.Background = active
            ? SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brush("#9053769C")
            : Brush("#01000000");
        organizeButton.Foreground = active && SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : GlassUi.Text;
        organizerDwellLine.Background = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brush("#D2E9FC");
        organizerDragOutline.BorderBrush = SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brush("#80B5DAF1");
        organizerDragOutline.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        organizerDwellLine.Visibility = active ? Visibility.Visible : Visibility.Hidden;
        organizerDwellFill.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        organizerDwellFill.ScaleX = active ? 1 : 0;
        if (waiting && SystemParameters.ClientAreaAnimation)
            organizerDwellFill.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, 1, fileExpandDelay.Interval) { FillBehavior = FillBehavior.Stop });
        RefreshOrganizerLift();
    }

    static void LiftCaption(TranslateTransform transform, double y)
    {
        if ((double)transform.GetAnimationBaseValue(TranslateTransform.YProperty) == y) return;
        double from = transform.Y;
        transform.Y = y;
        transform.BeginAnimation(TranslateTransform.YProperty, SystemParameters.ClientAreaAnimation
            ? new DoubleAnimation(from, y, TimeSpan.FromMilliseconds(115))
            {
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            }
            : null);
    }

    void OpenDesktopOrganizer()
    {
        if (fileDraggingOut || fileTransferBusy) return;
        desktopCategory = DesktopFileCategory.All;
        collectionPopup.PlacementTarget = organizeButton;
        ShowFilePanel("desktop", false, false);
    }

    void ApplyTileLanes()
    {
        // Keep at least a partial scrolling slot when there are more entries.
        int capacity = Math.Max(0, (int)Math.Floor((Viewport - IconSize - 6) / DockMath.Slot));
        favoriteCount = tiles.TakeWhile(tile => tile.Favorite).Count();
        if (favoriteCount == tiles.Count)
            capacity = Math.Max(capacity, (int)Math.Floor((Viewport - 6) / DockMath.Slot));
        fixedCount = Math.Min(favoriteCount, capacity);
        for (int index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            var lane = index < fixedCount ? fixedTiles : scrollingTiles;
            PlaceInLane(tile.Reflection, lane);
            PlaceInLane(tile.Button, lane);
            if (tile.Lamp is not null) PlaceInLane(tile.Lamp, lane);
        }
        fixedTiles.Width = FixedSpan;
        fixedTiles.Height = scrollingTiles.Height = 176;
        scrollingTiles.Width = ScrollingWidth;
        Set(scrollingTiles, FixedSpan, 0);
        scrollingTiles.Clip = new RectangleGeometry(new Rect(0, 0, ScrollingWidth, 176));
        Set(favoriteDivider, Math.Max(0, FixedSpan - 4), 108);
        favoriteDivider.Visibility = fixedCount > 0 && ScrollingCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        targetScroll = DockMath.ClampScroll(targetScroll, ScrollingCount, ScrollingWidth);
        scroll = DockMath.ClampScroll(scroll, ScrollingCount, ScrollingWidth);
    }

    static void PlaceInLane(FrameworkElement element, Canvas lane)
    {
        if (element.Parent == lane) return;
        if (element.Parent is Canvas previousLane) previousLane.Children.Remove(element);
        lane.Children.Add(element);
    }

    Tile? TileDropTarget(string id, Point position)
    {
        if (position.X < 0 || position.X > Viewport || position.Y < 60 || position.Y > 176)
            return null;
        bool pinned = state.FavoriteIds.Contains(id);
        var target = tiles.Where(tile => tile.Button.Visibility == Visibility.Visible)
            .MinBy(tile => Math.Abs(tile.Center - position.X));
        return target is not null && state.FavoriteIds.Contains(target.Item.Id) == pinned ? target : null;
    }

    void ShowDropMarker(string id, Tile? target)
    {
        if (target is null || target.Item.Id == id)
        {
            dropMarker.Visibility = Visibility.Collapsed;
            return;
        }
        int sourceIndex = tiles.FindIndex(tile => tile.Item.Id == id);
        double side = sourceIndex < tiles.IndexOf(target) ? 1 : -1;
        Set(dropMarker, Math.Clamp(target.Center + side * (target.Button.Width / 2 + 4), 0, Math.Max(0, Viewport - 2)), 97);
        dropMarker.Visibility = Visibility.Visible;
    }

    bool ReorderTile(string sourceId, string targetId)
    {
        if (sourceId == targetId) return false;
        var item = state.Items.FirstOrDefault(entry => entry.Id == sourceId);
        int target = state.Items.FindIndex(entry => entry.Id == targetId);
        if (item is null || target < 0) return false;
        state.Items.Remove(item);
        state.Items.Insert(target, item);
        Rebuild();
        Save();
        return true;
    }

    void PositionDockHandle()
    {
        FinishDockEdgeMotion();
        var edge = Canvas.GetTop(toolbarBackground);
        Set(collapseDockButton, (Width - collapseDockButton.Width) / 2,
            state.TopEdge ? edge + toolbarBackground.Height - collapseDockButton.Height : edge - 8);
        ((ScaleTransform)collapseFace.Child.RenderTransform).ScaleY = state.TopEdge ? -1 : 1;
        if (dockHandle is null) return;
        var area = NativeServices.WorkingArea(this);
        double width = Math.Min(78, area.Width), height = Math.Min(23, area.Height);
        dockHandle.Width = width;
        dockHandle.Height = height;
        dockHandle.Left = Math.Clamp(Left + (Width - width) / 2, area.Left, area.Right - width);
        dockHandle.Top = state.TopEdge ? area.Top : area.Bottom - height;
        handleFace!.VerticalAlignment = state.TopEdge ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        handleFace.CornerRadius = state.TopEdge ? new CornerRadius(0, 0, 7, 7) : new CornerRadius(7, 7, 0, 0);
        ((ScaleTransform)handleFace.Child.RenderTransform).ScaleY = state.TopEdge ? 1 : -1;
    }

    static Path ToolbarStroke(string geometry)
    {
        var stroke = new Path
        {
            Data = Geometry.Parse(geometry), StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(.5, .5),
            IsHitTestVisible = false
        };
        stroke.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
        });
        return stroke;
    }

    static Button EdgeControl(string label, double width, out Border face, bool collapsed = false)
    {
        var button = Control("", label, width);
        button.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : GlassUi.Text;
        button.Effect = null;
        var hitArea = new FrameworkElementFactory(typeof(Border));
        hitArea.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        hitArea.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = hitArea };

        var normal = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brush("#A42D495D");
        var hover = SystemParameters.HighContrast ? SystemColors.WindowBrush : Brush("#8B345874");
        var highlight = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brush("#83BCDEF5");
        var feedback = new Style(typeof(Border));
        if (collapsed || SystemParameters.HighContrast)
        {
            feedback.Setters.Add(new Setter(Border.BackgroundProperty, normal));
            feedback.Setters.Add(new Setter(Border.BorderBrushProperty, highlight));
        }
        foreach (var state in new[] { IsMouseOverProperty, IsKeyboardFocusedProperty, Button.IsPressedProperty })
        {
            var trigger = new DataTrigger
            {
                Binding = new Binding(state.Name)
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
                },
                Value = true
            };
            trigger.Setters.Add(new Setter(Border.BackgroundProperty,
                state == Button.IsPressedProperty && !SystemParameters.HighContrast ? normal : hover));
            trigger.Setters.Add(new Setter(Border.BorderBrushProperty, highlight));
            feedback.Triggers.Add(trigger);
        }

        // Keep the clickable area stable while only the glass face changes.
        var chevron = ToolbarStroke("M 0,0 L 4,3.5 L 8,0");
        chevron.RenderTransform = new ScaleTransform();
        if (collapsed) chevron.Margin = new Thickness(0, 0, 0, 1);
        if (!collapsed)
            chevron.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = .72
            };
        face = new Border
        {
            Child = chevron, Style = feedback, Width = collapsed ? 54 : 30, Height = collapsed ? 13 : 14,
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };
        if (collapsed && !SystemParameters.HighContrast) AnimateHandleHighlight(button, face, normal.Color, highlight.Color);
        button.Content = face;
        return button;
    }

    static void AnimateHandleHighlight(Button button, Border face, Color baseGlass, Color baseRim)
    {
        var glass = new SolidColorBrush(baseGlass);
        var rim = new SolidColorBrush(baseRim);
        face.Background = glass;
        face.BorderBrush = rim;

        void Change(SolidColorBrush brush, Color target, bool animate)
        {
            var previous = brush.Color;
            brush.Color = target;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, animate && SystemParameters.ClientAreaAnimation
                ? new ColorAnimation(previous, target, TimeSpan.FromMilliseconds(140))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                }
                : null);
        }

        void Highlight(bool active, bool animate)
        {
            Change(glass, active ? Color.FromArgb(190, 53, 85, 108) : baseGlass, animate);
            Change(rim, active ? Color.FromArgb(168, baseRim.R, baseRim.G, baseRim.B) : baseRim, animate);
        }

        button.MouseEnter += (_, _) => Highlight(true, true);
        button.MouseLeave += (_, _) => Highlight(false, true);
        button.IsVisibleChanged += (_, _) =>
        {
            if (!button.IsVisible) Highlight(false, false);
        };
    }

    Window EnsureDockHandle()
    {
        if (dockHandle is null)
        {
            handleButton = EdgeControl("展开 Dock", 78, out handleFace, collapsed: true);
            handleButton.Width = handleButton.Height = double.NaN;
            handleButton.Click += (_, _) => RevealDock();
            var exit = new ContextMenu();
            exit.Items.Add(Entry("退出控制中心", Close));
            GlassUi.Menu(exit);
            handleButton.ContextMenu = exit;
            dockHandle = new Window
            {
                Title = "展开 Dock", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = Brushes.Transparent, Content = handleButton,
                ShowActivated = false, ShowInTaskbar = false, Topmost = false
            };
            dockHandle.SourceInitialized += (_, _) =>
            {
                DesktopLayer.Attach(dockHandle);
                ((HwndSource)PresentationSource.FromVisual(dockHandle)).AddHook(KeepHandlePassive);
            };
        }
        return dockHandle;
    }

    static IntPtr KeepHandlePassive(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (message != 0x0021) return IntPtr.Zero; // WM_MOUSEACTIVATE
        handled = true;
        return new IntPtr(3); // MA_NOACTIVATE, still dispatch the click
    }

    public void RevealDock() => ApplyDockVisibility(true);

    internal void ToggleFromShortcut()
    {
        if (!loaded) return;
        if (shortcutRecalled && !state.DockCollapsed)
        {
            ApplyDockVisibility(false);
            if (!state.DockCollapsed) return;
            if (dockEdgeMotion is { } collapse)
                DesktopLayer.SetShortcutPresentation(collapse.Surface, true);
            LifecycleLog.Write("shortcut-collapsed");
            return;
        }

        RevealDock();
        shortcutRecalled = true;
        DesktopLayer.SetShortcutPresentation(this, true);
        if (dockEdgeMotion is { } motion)
            DesktopLayer.SetShortcutPresentation(motion.Surface, true);
        shortcutPresentation.Stop();
        shortcutPresentation.Start();
        LifecycleLog.Write("shortcut-revealed");
    }

    void EndShortcutPresentation()
    {
        shortcutPresentation.Stop();
        DesktopLayer.SetShortcutPresentation(this, false);
    }

    void ApplyDockVisibility(bool expand, bool animate = true)
    {
        if (!loaded || (!expand && (internalDrag || fileDraggingOut || fileDragActive || fileTransferBusy))) return;
        if (expand && !state.DockCollapsed && IsVisible) return;
        if (!expand && animate && state.DockCollapsed) return;
        ++dockTransition;
        state.DockCollapsed = !expand;
        if (!expand)
        {
            shortcutRecalled = false;
            CloseLaunchIssue();
            if (activeMenu is { } context) context.IsOpen = false;
        }
        hovering = false;
        nameBubble.Visibility = Visibility.Collapsed;
        root.IsHitTestVisible = false;
        CloseCollection();

        if (expand) RefreshLinkedStatus();
        if (animate && SystemParameters.ClientAreaAnimation)
            AnimateDockEdge(expand);
        else if (dockEdgeMotion is not null)
            FinishDockEdgeMotion();
        else
            SettleDockVisibility();
        Save();
    }

    void SettleDockVisibility()
    {
        if (state.DockCollapsed)
        {
            Hide();
            EndShortcutPresentation();
            root.Opacity = 0;
            root.IsHitTestVisible = false;
            Opacity = 1;
            var handle = EnsureDockHandle();
            PositionDockHandle();
            handle.BeginAnimation(OpacityProperty, null);
            handle.Opacity = 1;
            if (!handle.IsVisible) handle.Show();
            return;
        }

        if (dockHandle is { } hiddenHandle)
        {
            hiddenHandle.Hide();
            hiddenHandle.BeginAnimation(OpacityProperty, null);
            hiddenHandle.Opacity = 1;
        }
        root.Opacity = Opacity = 1;
        root.IsHitTestVisible = true;
        WindowState = WindowState.Normal;
        if (!IsVisible) Show();
    }
}

