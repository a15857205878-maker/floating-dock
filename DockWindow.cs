using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace FloatingDock;

public sealed partial class DockWindow : Window
{
    const string DragFormat = "FloatingDock.Item";
    const double IconSize = 56, WindowHeight = 184;
    double LeftInset => Islands.AppLeft;
    double RightInset => Width-Islands.AppRight;
    readonly DockState state;
    readonly string statePath;
    readonly Canvas root = new();
    readonly Border toolbarBackground = new() { Name = "ToolbarBackground", Background = Brush("#184A6D88"), BorderBrush = Brush("#BBE6F4FF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Height = 78 };
    readonly Canvas strip = new() { AllowDrop = true };
    readonly Border stripHit = new() { Background = Brush("#01000000"), Height = 96 };
    readonly Border nameBubble = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly TextBlock nameText = new();
    readonly List<Tile> tiles = [];
    readonly Dictionary<string, (string Stamp, ImageSource? Image)> icons = new(StringComparer.OrdinalIgnoreCase);
    readonly System.Windows.Threading.DispatcherTimer iconRefresh = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Stopwatch time = Stopwatch.StartNew();
    readonly Button grip, previous, next, add, menu;
    readonly Border progress = new() { Height = 1, CornerRadius = new CornerRadius(1), Background = Brush("#408EA8BE") };
    readonly TextBlock empty = new() { Text = "拖入应用，或从更多菜单添加", TextTrimming=TextTrimming.CharacterEllipsis, ToolTip="拖入应用，或从右侧更多菜单添加；应用右键可调整分组", Foreground = Brush("#B5C3D5"), FontSize = 13, IsHitTestVisible = false };
    double scroll, targetScroll, lastTime, lastWheel = -10;
    bool hovering, internalDrag, loaded, savingError, blockClick, iconsClosed;
    ContextMenu? activeMenu;
    bool menuOpen => activeMenu is not null;
    Point pointer, pressedAt;
    Tile? pressed;
    double Viewport => Math.Max(1, Width - LeftInset - RightInset);

    sealed class Tile(DockItem item, Button button, Image image, bool favorite)
    {
        public DockItem Item = item;
        public Button Button = button;
        public Image Image = image;
        public readonly bool Favorite = favorite;
        public ScaleTransform? FaceScale;
        public TranslateTransform? FaceLift;
        public double PreviewOffset;
        public string? LinkedId;
        public string LaunchLabel = "";
        public Border? Lamp;
        public Border Reflection = new(){Name="IconReflection",IsHitTestVisible=false,Opacity=.20,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1,-1),OpacityMask=new LinearGradientBrush(Colors.Transparent,Colors.Black,90),Clip=new RectangleGeometry()};
        public double Scale = 1;
        public double Center;
        public string IconStamp = "";
        public int IconRequest;
    }

    public DockWindow(DockState state, string statePath, WeatherService? weatherService = null,
        Func<IMusicSession>? musicFactory = null, DockLinkedApps? linkedApps = null)
    {
        this.state = state; this.statePath = statePath;
        this.linkedApps = linkedApps ?? DockLinkedApps.Load(AppContext.BaseDirectory);
        this.musicFactory=musicFactory??(()=>new MusicSessionBridge());
        Title = "悬浮控制中心"; Width = state.Width; Height = WindowHeight;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = false; ShowInTaskbar = false; ShowActivated = false;
        SourceInitialized += (_, _) => DesktopLayer.Attach(this);
        FontFamily = new FontFamily("Microsoft YaHei UI"); Content = root; AllowDrop = true;

        root.Children.Add(toolbarBackground); root.Children.Add(strip);
        toolbarBackground.Child = new AcrylicMaterial(this, true);
        toolbarBackground.BorderBrush = new LinearGradientBrush(Color.FromArgb(175,239,249,255),Color.FromArgb(65,180,217,243),90);
        toolbarBackground.Effect = new DropShadowEffect { Color = Color.FromRgb(20,43,66), BlurRadius = 12, ShadowDepth = 3, Opacity = .20 };
        strip.Children.Add(stripHit);
        strip.Children.Add(scrollingTiles);
        strip.Children.Add(fixedTiles);
        strip.Children.Add(empty);

        grip = Control("⠿", "拖动这里移动控制中心", 24);
        previous = Control("‹", "向左滚动", 24); next = Control("›", "向右滚动", 24);
        add = Control("＋", "添加快捷方式", 32); menu = Control("⋯", "控制中心菜单", 32);
        previous.Visibility = next.Visibility = Visibility.Collapsed;
        foreach (var button in new[] { grip, previous, next, add, menu }) root.Children.Add(button);
        InitializeWeather(weatherService ?? new WeatherService());
        InitializeCollection();
        InitializeIslands();
        InitializeDesktopCards();
        InitializeMusic();
        InitializeDockShelf();
        InitializeLinkedStatus();
        grip.Cursor = Cursors.SizeAll;
        grip.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1) return;
            hovering = false; nameBubble.Visibility = Visibility.Collapsed;
            DragMove();
            var area = NativeServices.WorkingArea(this);
            this.state.TopEdge = Top + Height / 2 < area.Top + area.Height / 2;
            KeepVisible(); Layout(); Save(); e.Handled = true;
        };
        previous.Click += (_, _) => ScrollBy(-DockMath.Slot * 3);
        next.Click += (_, _) => ScrollBy(DockMath.Slot * 3);
        add.Click += (_, _) => AddFiles();
        menu.Click += (_, _) => OpenMenu(menu, BuildMenu());
        stripHit.MouseRightButtonUp += (_, e) => { OpenMenu(stripHit, BuildMenu()); e.Handled = true; };
        PreviewMouseWheel += (_, e) => { CloseLaunchIssue(); ScrollBy(-e.Delta / 120.0 * DockMath.Slot * 1.55); e.Handled = true; };
        strip.PreviewMouseMove += (_, e) =>
        {
            pointer = e.GetPosition(strip); hovering = true;
            if (pressed is not null && e.LeftButton == MouseButtonState.Pressed && !internalDrag &&
                (Math.Abs(pointer.X - pressedAt.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(pointer.Y - pressedAt.Y) > SystemParameters.MinimumVerticalDragDistance))
            {
                var tile = pressed; pressed = null; internalDrag = true; blockClick = true;
                nameBubble.Visibility = Visibility.Collapsed;
                tile.Button.ReleaseMouseCapture();
                try { DragDrop.DoDragDrop(tile.Button, new DataObject(DragFormat, tile.Item.Id), DragDropEffects.Move); }
                finally
                {
                    ClearFavoritePreview();
                    internalDrag = false;
                    hovering = false;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () => blockClick = false);
                }
                e.Handled = true;
            }
        };
        strip.MouseLeave += (_, _) => { hovering = false; nameBubble.Visibility = Visibility.Collapsed; };
        PreviewMouseLeftButtonUp += (_, _) => pressed = null;
        PreviewDragOver += OnDragOver; PreviewDrop += OnDrop;
        DragLeave+=(_,_)=>ScheduleFileDragLeave();

        nameText.Foreground = Brush("#F4F6FA"); nameText.FontSize = 12; nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        nameBubble.Child = nameText; nameBubble.Padding = new Thickness(12, 6, 12, 6); nameBubble.CornerRadius = new CornerRadius(9);
        nameBubble.Background = Brush("#F2222C3F"); nameBubble.BorderBrush = Brush("#667286"); nameBubble.BorderThickness = new Thickness(1);
        root.Children.Add(nameBubble); root.Children.Add(progress);
        Loaded += (_, _) =>
        {
            loaded = true;
            bool restyle=DockVisualUpgrade.Apply(state);
            bool physicalPosition=state.PixelX is double px&&state.PixelY is double py&&double.IsFinite(px)&&double.IsFinite(py);
            bool logicalPosition=state.X is double x&&state.Y is double y&&double.IsFinite(x)&&double.IsFinite(y);
            // Select the stored monitor before measuring its work area or migrating the dock.
            if(physicalPosition)NativeServices.RestorePixelPosition(this,state.PixelX!.Value,state.PixelY!.Value);
            else if(logicalPosition){Left=state.X!.Value;Top=state.Y!.Value;}
            var area = NativeServices.WorkingArea(this);
            Width = Math.Min(state.Width, Math.Max(1,area.Width - 24));
            if(restyle&&!state.TopEdge){var raised=DockLayout.FloatingPosition(new(area.Left,area.Top,area.Width,area.Height),Width,Height,false);Left=raised.X;Top=raised.Y;}
            else if(physicalPosition||logicalPosition)KeepVisible();
            else {var position=DockLayout.FloatingPosition(new(area.Left,area.Top,area.Width,area.Height),Width,Height,state.TopEdge);Left=position.X;Top=position.Y;}
            Layout(); Save();
            CompositionTarget.Rendering += Render;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;SystemEvents.UserPreferenceChanged+=DockWorkAreaChanged;
            StartWeather();
            iconRefresh.Start();
            StartDesktopSync();
            if (state.DockCollapsed) ApplyDockVisibility(false, animate: false);
        };
        DpiChanged += (_, _) => { if (loaded) Dispatcher.BeginInvoke(() => { KeepVisible(); Layout(); Save(); }); };
        iconRefresh.Tick += (_, _) => RefreshIcons();
        Closing += (_, _) => Save();
        Closed += (_, _) => { loaded = false; iconsClosed = true; iconRefresh.Stop(); StopDesktopSync(); StopWeather(); CompositionTarget.Rendering -= Render; SystemEvents.DisplaySettingsChanged -= DisplayChanged;SystemEvents.UserPreferenceChanged-=DockWorkAreaChanged; };
        Rebuild(); Layout();
        SizeChanged += (_, _) => Layout();
    }

    static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

    static Button Control(string text, string label, double width)
    {
        var button = new Button { Content = text, Width = width, Height = 36, FontSize = 23, Foreground = Brush("#C2CEE0"), Background = Brush("#01000000"), BorderThickness = new Thickness(0), Cursor = Cursors.Hand, ToolTip = label, FocusVisualStyle = null };
        button.Template = TransparentButtonTemplate(); AutomationProperties.SetName(button, label);
        button.Foreground = Brush("#F1F5FC");
        button.Effect = new DropShadowEffect { Color = Color.FromRgb(35,65,90), BlurRadius = 4, ShadowDepth = 1, Opacity = .45 };
        return button;
    }

    static ControlTemplate TransparentButtonTemplate(double radius=10)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(content); template.VisualTree = border;
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(OpacityProperty, .82)); template.Triggers.Add(hover);
        var focus = new Trigger { Property = IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(BackgroundProperty, Brush("#406582A5"))); template.Triggers.Add(focus);
        return template;
    }

    void Layout()
    {
        LayoutIslands();
    }

    static void Set(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); }

    void Rebuild()
    {
        CloseLaunchIssue();
        ClearFavoritePreview();
        fixedTiles.Children.Clear();
        scrollingTiles.Children.Clear();
        tiles.Clear();
        groupButton.Content = GroupCaption;
        foreach (var item in DockGroups.LauncherItems(state))
        {
            var image = new Image { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            var button = new Button { Width = IconSize, Height = IconSize, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Content = image, Cursor = Cursors.Hand, FocusVisualStyle = null };
            button.Background=Brushes.Transparent;button.Template = TransparentButtonTemplate(14);
            AutomationProperties.SetName(button, item.Name);
            var tile = new Tile(item, button, image, state.FavoriteIds.Contains(item.Id))
            {
                LinkedId = linkedApps?.FindId(item.Path)
            };
            if (tile.Favorite) PrepareFavoriteFace(tile);
            PrepareLinkedTile(tile);
            tile.Reflection.Background = new VisualBrush(button){Stretch=Stretch.Fill};
            button.PreviewMouseLeftButtonDown += (_, e) => { blockClick = false; pressed = tile; pressedAt = e.GetPosition(strip); };
            button.LostMouseCapture += (_, _) => { if (pressed == tile) pressed = null; };
            button.Click += (_, _) => { if (!internalDrag && !blockClick) OpenDockItem(tile); };
            button.MouseRightButtonUp += (_, e) => { pressed = null; OpenMenu(button, ItemMenu(tile)); e.Handled = true; };
            button.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key != Key.Apps && !(key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))) return;
                var context = ItemMenu(tile);
                context.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                OpenMenu(button, context);
                e.Handled = true;
            };
            button.GotKeyboardFocus += (_, _) => { ShowName(tile); };
            button.LostKeyboardFocus += (_, _) => { nameBubble.Visibility = Visibility.Collapsed; };
            tiles.Add(tile);
            tile.IconStamp = IconStamp(item.Path);
            if (icons.TryGetValue(item.Path, out var cached) && cached.Stamp == tile.IconStamp) AssignIcon(tile, cached.Image);
            else LoadIcon(tile);
        }
        ApplyTileLanes();
        empty.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    static string IconStamp(string path)
    {
        try { var file = new FileInfo(path); return file.Exists ? $"{file.Length}:{file.LastWriteTimeUtc.Ticks}" : "missing"; }
        catch (IOException) { return "unavailable"; }
        catch (UnauthorizedAccessException) { return "unavailable"; }
    }

    void RefreshIcons(bool force = false)
    {
        foreach (var tile in tiles)
        {
            var stamp = IconStamp(tile.Item.Path);
            if (!force && stamp == tile.IconStamp) continue;
            tile.IconStamp = stamp;
            icons.Remove(tile.Item.Path);
            LoadIcon(tile);
        }
    }

    async void LoadIcon(Tile tile)
    {
        var request = ++tile.IconRequest;
        var stamp = tile.IconStamp;
        try
        {
            var icon = await Task.Run(() => NativeServices.IconFor(tile.Item.Path));
            if (Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() => {
                // A later change or rebuild must not be overwritten by an old extraction.
                if (iconsClosed || !tiles.Contains(tile) || request != tile.IconRequest || stamp != IconStamp(tile.Item.Path)) return;
                icons[tile.Item.Path] = (stamp, icon); AssignIcon(tile, icon);
            });
        }
        catch (TaskCanceledException) { }
        catch (Exception)
        {
            if (!Dispatcher.HasShutdownStarted) await Dispatcher.InvokeAsync(() => { if (tiles.Contains(tile) && request == tile.IconRequest) AssignIcon(tile, null); });
        }
    }

    static void AssignIcon(Tile tile, ImageSource? icon)
    {
        FrameworkElement face;
        if (icon is not null) { tile.Image.Source = icon; face = tile.Image; }
        else face = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(13), Background = Brush("#408CB8CC"), Child = new TextBlock { Text = string.IsNullOrEmpty(tile.Item.Name) ? "?" : tile.Item.Name[..1], FontSize = 24, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };

        if (tile.Favorite && tile.Button.Content is Grid wrapper)
        {
            wrapper.Children.Clear();
            wrapper.Children.Add(face);
        }
        else tile.Button.Content = face;
    }

    void ScrollBy(double amount)
    {
        targetScroll = DockMath.ClampScroll(targetScroll + amount, ScrollingCount, ScrollingWidth);
        lastWheel = time.Elapsed.TotalSeconds; nameBubble.Visibility = Visibility.Collapsed;
    }

    void Render(object? sender, EventArgs e)
    {
        double now = time.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - lastTime, 0, .05); lastTime = now;
        double blend = 1 - Math.Exp(-16 * dt);
        double reorderBlend = 1 - Math.Exp(-23 * dt);
        scroll += (targetScroll - scroll) * blend;
        if (Math.Abs(targetScroll - scroll) < .05) scroll = targetScroll;
        UpdateFavoriteInsertion();
        bool active = hovering && !menuOpen && !launchIssue.IsOpen && !internalDrag && favoriteDragId is null && now - lastWheel > .15;
        bool scrollHover = active && pointer.X >= FixedSpan && !OverFavoriteLane(pointer.X, fixedLane: false);
        double baseOrigin = ScrollingOrigin;
        double totalExtra = 0;
        for (int i = fixedCount; i < tiles.Count; i++)
        {
            var tile = tiles[i];
            double center = FixedSpan + baseOrigin + (i - fixedCount + .5) * DockMath.Slot;
            double desired = DockMath.Magnification(pointer.X - center, scrollHover && !tile.Favorite);
            if (favoriteDragId is not null) tile.Scale = 1;
            tile.Scale += (desired - tile.Scale) * blend; totalExtra += IconSize * (tile.Scale - 1);
        }
        double anchor = active ? Math.Clamp((pointer.X - FixedSpan) / ScrollingWidth, 0, 1) : .5;
        double x = baseOrigin - totalExtra * anchor;
        Tile? nearest = null; double nearestDistance = double.MaxValue;
        for (int index = 0; index < tiles.Count; index++)
        {
            var tile = tiles[index];
            bool pinned = index < fixedCount;
            if (pinned) tile.Scale = 1;
            double size = IconSize * tile.Scale;
            double slot = DockMath.Slot + size - IconSize;
            double center = pinned ? (index + .5) * DockMath.Slot : x + slot / 2;
            tile.Center = tile.Favorite ? FavoriteSlotCenter(index) : center + (pinned ? 0 : FixedSpan);
            bool drawInFixedLane = pinned;
            if (tile.Favorite)
            {
                var visual=PositionFavorite(tile,index,reorderBlend);
                drawInFixedLane=visual.Fixed;
                center=visual.Center-(drawInFixedLane ? 0 : FixedSpan);
                bool over = active && OverFavoriteLane(pointer.X, pinned)
                    && Math.Abs(pointer.X-tile.Center)<IconSize/2 && pointer.Y is >= 94 and <= 150;
                SetFavoriteFace(tile, over, pressed==tile || tile.Button.IsPressed, blend);
            }
            double y = 150 - size;
            tile.Button.Width = size; tile.Button.Height = size; tile.Image.Width = size; tile.Image.Height = size; Set(tile.Button, center - size / 2, y);
            tile.Button.Visibility = !drawInFixedLane && (center + size / 2 < 0 || center - size / 2 > ScrollingWidth) ? Visibility.Hidden : Visibility.Visible;
            tile.Button.Opacity = drawInFixedLane ? 1 : Math.Clamp(Math.Min(center + 8, ScrollingWidth - center + 8) / 28, .15, 1);
            if(tile.Item.Id==favoriteDragId && favoriteDestination>=0) tile.Button.Opacity=0;
            tile.Reflection.Width=tile.Reflection.Height=size;Set(tile.Reflection,center-size/2,152);((RectangleGeometry)tile.Reflection.Clip).Rect=new Rect(0,Math.Max(0,size-21),size,Math.Min(21,size));
            tile.Reflection.Visibility=tile.Button.Visibility;tile.Reflection.Opacity=.20*tile.Button.Opacity;
            if (tile.Lamp is { } lamp)
            {
                lamp.Visibility = tile.LaunchLabel.Length > 0 && tile.Button.Visibility == Visibility.Visible && tile.Button.Opacity > 0
                    ? Visibility.Visible : Visibility.Hidden;
                Set(lamp, center - lamp.Width / 2, 155);
            }
            double distance = Math.Abs(pointer.X - tile.Center);
            if (tile.Button.Visibility == Visibility.Visible && pinned == (pointer.X < FixedSpan) && distance < nearestDistance)
            { nearestDistance = distance; nearest = tile; }
            if (!pinned) x += slot;
        }
        if (active && nearest is not null && nearestDistance < 58 && pointer.Y >= 25 && pointer.Y <= 150) ShowName(nearest);
        else if (!tiles.Any(t => t.Button.IsKeyboardFocused)) nameBubble.Visibility = Visibility.Collapsed;
        double max = Math.Max(0, ScrollingCount * DockMath.Slot - ScrollingWidth);
        previous.IsEnabled = targetScroll > .1; next.IsEnabled = targetScroll < max - .1;
        previous.Opacity = previous.IsEnabled ? .85 : .24; next.Opacity = next.IsEnabled ? .85 : .24;
        progress.Visibility = max > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (max > 0) { progress.Width = Math.Min(ScrollingWidth, Math.Max(24, ScrollingWidth * ScrollingWidth / (ScrollingCount * DockMath.Slot))); Set(progress, LeftInset + FixedSpan + (ScrollingWidth - progress.Width) * scroll / max, 174); }
    }

    void ShowName(Tile tile)
    {
        nameText.Text = tile.LaunchLabel.Length == 0 ? tile.Item.Name : tile.Item.Name + " · " + tile.LaunchLabel;
        nameBubble.MaxWidth = Math.Min(300, Width - 32);
        nameBubble.Measure(new Size(nameBubble.MaxWidth, 45));
        double x = Math.Clamp(LeftInset + tile.Center - nameBubble.DesiredSize.Width / 2, 12, Math.Max(12, Width - nameBubble.DesiredSize.Width - 12));
        Set(nameBubble, x, 12); nameBubble.Visibility = Visibility.Visible;
    }

    void OpenMenu(FrameworkElement owner, ContextMenu context)
    {
        nameBubble.Visibility = Visibility.Collapsed;
        activeMenu = context;
        context.Closed += (_, _) =>
        {
            if (activeMenu == context) activeMenu = null;
            hovering = false;
            if (shortcutRefreshDeferred) RefreshDesktopEntries();
        };
        GlassUi.Menu(context);
        owner.ContextMenu = context;
        context.PlacementTarget = owner; context.IsOpen = true;
    }

    static MenuItem Entry(string label, Action action)
    {
        var entry = new MenuItem { Header = label }; entry.Click += (_, _) => action(); return entry;
    }

    ContextMenu BuildMenu()
    {
        var context = new ContextMenu();
        var search = Entry("搜索应用或文件", () => ShowFilePanel("recent", true, true));
        var files = Entry("打开文件收纳", ToggleCollection);
        var downloads = Entry("最近下载", () => ShowFilePanel("downloads", false, false));
        var organize = Entry("桌面分类与整理", OpenDesktopOrganizer);
        search.Icon = DailyMenuIcon("\uE721");
        files.Icon = DailyMenuIcon("\uE8B7");
        downloads.Icon = DailyMenuIcon("\uE896");
        organize.Icon = DailyMenuIcon("\uE8FD");
        foreach (var daily in new[] { search, files, downloads, organize })
            context.Items.Add(daily);

        context.Items.Add(new Separator());
        var groups = new MenuItem { Header = "应用分组", Icon = DailyMenuIcon("\uE80A") };
        AddGroupChoices(groups);
        context.Items.Add(groups);
        context.Items.Add(Entry("收起 Dock", () => ApplyDockVisibility(false)));

        var more = new MenuItem { Header = "更多" };
        more.Items.Add(BuildEntryManagementMenu());
        more.Items.Add(BuildWidgetMenu());
        more.Items.Add(BuildDisplayMenu());

        var system = new MenuItem { Header = "系统入口" };
        foreach (var entry in SystemEntries)
            system.Items.Add(Entry(entry.Name, () => OpenSystemEntry(entry.Target)));
        more.Items.Add(system);
        more.Items.Add(new Separator());
        more.Items.Add(Entry("退出控制中心", Close));
        context.Items.Add(more);
        return context;
    }

    MenuItem BuildEntryManagementMenu()
    {
        var management = new MenuItem { Header = "入口管理" };
        management.Items.Add(Entry("添加快捷方式…", AddFiles));
        management.Items.Add(Entry("导入桌面快捷方式", () => AddPaths(NativeServices.DesktopShortcuts())));
        var autoImport = new MenuItem { Header = "自动同步桌面快捷方式", IsCheckable = true, IsChecked = state.AutoImportDesktop };
        autoImport.Click += (_, _) =>
        {
            state.AutoImportDesktop = autoImport.IsChecked;
            Save();
            if (state.AutoImportDesktop) RefreshDesktopEntries();
        };
        management.Items.Add(autoImport);
        management.Items.Add(Entry("刷新图标", () => RefreshIcons(true)));
        return management;
    }

    MenuItem BuildWidgetMenu()
    {
        var widgets = new MenuItem { Header = "桌面组件" };
        var weather = Entry("显示天气组件", () => SetWeatherVisible(!state.WeatherWidget.Visible));
        weather.IsCheckable = true;
        weather.IsChecked = state.WeatherWidget.Visible;
        widgets.Items.Add(weather);
        AddDesktopCardMenus(widgets);
        AddMusicMenu(widgets);
        return widgets;
    }

    MenuItem BuildDisplayMenu()
    {
        var display = new MenuItem { Header = "显示与位置" };
        display.Items.Add(Entry("放到屏幕顶部", () => Place(true)));
        display.Items.Add(Entry("放到屏幕底部", () => Place(false)));
        var widths = new MenuItem { Header = "底座宽度" };
        foreach (var (label, width) in new[] { ("紧凑", 700d), ("标准", 940d), ("宽", 1180d) })
            widths.Items.Add(Entry(label, () =>
            {
                var area = NativeServices.WorkingArea(this);
                Width = Math.Min(width, area.Width - 24);
                state.Width = Width;
                KeepVisible(); Layout(); Save();
            }));
        display.Items.Add(widths);
        display.Items.Add(new Separator());
        display.Items.Add(Entry("显示桌面", () => Try(DesktopLauncher.ShowDesktop)));
        display.Items.Add(Entry("向左翻页", () => ScrollBy(-DockMath.Slot * 3)));
        display.Items.Add(Entry("向右翻页", () => ScrollBy(DockMath.Slot * 3)));
        return display;
    }

    static TextBlock DailyMenuIcon(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 13,
        Foreground = GlassUi.Text,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    ContextMenu ItemMenu(Tile tile)
    {
        var context = new ContextMenu();
        context.Items.Add(new MenuItem
        {
            Header = new TextBlock { Text = tile.Item.Name, MaxWidth = 190, TextTrimming = TextTrimming.CharacterEllipsis },
            IsEnabled = false
        });
        context.Items.Add(new Separator());

        bool favorite = state.FavoriteIds.Contains(tile.Item.Id);
        var pin = Entry(favorite ? "取消固定" : "固定到常用", () => SetFavorite(tile.Item, !favorite));
        pin.Icon = DailyMenuIcon(favorite ? "\uE77A" : "\uE718");
        context.Items.Add(pin);
        var groups = new MenuItem { Header = "分组 · " + DockGroups.Name(tile.Item.Group), Icon = DailyMenuIcon("\uE80A") };
        AddGroupChoices(groups, tile.Item);
        context.Items.Add(groups);

        var more = new MenuItem { Header = "更多" };
        more.Items.Add(Entry("打开", () => OpenDockItem(tile)));
        more.Items.Add(Entry("打开所在位置", () => Try(() => NativeServices.Reveal(tile.Item.Path))));
        more.Items.Add(Entry("重命名…", () => Rename(tile.Item)));
        more.Items.Add(new Separator());
        more.Items.Add(Entry("移出控制中心", () =>
        {
            state.IgnoredDesktopShortcuts.Add(tile.Item.Path);
            state.FavoriteIds.Remove(tile.Item.Id);
            state.Items.Remove(tile.Item);
            Rebuild(); Save();
        }));
        context.Items.Add(more);
        return context;
    }

    void SetFavorite(DockItem item, bool pinned)
    {
        if (pinned == state.FavoriteIds.Contains(item.Id)) return;
        if (pinned) state.FavoriteIds.Add(item.Id);
        else state.FavoriteIds.Remove(item.Id);
        Rebuild(); Save();
    }

    void Rename(DockItem item)
    {
        var dialog = new Window { Owner = this, Title = "重命名快捷方式", Width = 360, Height = 160, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#F6F7FA"), Topmost = true };
        var panel = new StackPanel { Margin = new Thickness(18) };
        var text = new TextBox { Text = item.Name, FontSize = 14, Padding = new Thickness(6), MaxLength = 100 };
        var save = new Button { Content = "保存", Padding = new Thickness(20, 6, 20, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsDefault = true };
        panel.Children.Add(text); panel.Children.Add(save); dialog.Content = panel;
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) { item.Name = text.Text.Trim(); dialog.DialogResult = true; } };
        dialog.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        if (dialog.ShowDialog() == true) { Rebuild(); Save(); }
    }

    void AddFiles()
    {
        var dialog = new OpenFileDialog { Title = "添加到悬浮控制中心", Multiselect = true, Filter = "快捷方式和应用|*.lnk;*.url;*.exe;*.appref-ms|所有文件|*.*", DereferenceLinks = false };
        if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames);
    }

    void AddPaths(IEnumerable<string> paths) => Try(() => {
        var existing=paths.Where(p=>File.Exists(p)||Directory.Exists(p)).ToArray();
        state.IgnoredDesktopShortcuts.RemoveAll(p=>existing.Contains(p,StringComparer.OrdinalIgnoreCase));
        DockCatalog.Merge(state,existing);if(state.SelectedGroup!="all")foreach(var item in state.Items.Where(i=>existing.Contains(i.Path,StringComparer.OrdinalIgnoreCase)))item.Group=state.SelectedGroup;Rebuild();Save();WatchShortcutSources();
    });

    void OnDragOver(object sender, DragEventArgs e)
    {
        if(IsFileSurface(e.OriginalSource as DependencyObject)){HandleFileDragOver(sender,e);return;}
        var source=e.OriginalSource as DependencyObject;
        bool organizer=IsOrganizerButton(source);
        if((organizer||IsFilesButton(source))&&e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ExpandFileButtonForDrag(organizer);e.Effects=e.AllowedEffects&DragDropEffects.Link;if(e.Effects==DragDropEffects.None)e.Effects=e.AllowedEffects&DragDropEffects.Copy;e.Handled=true;return;
        }
        if (quickDropTemporary && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ScheduleFileDragLeave(restart: false);
            e.Effects=DragDropEffects.None;
            e.Handled=true;
            return;
        }
        EndFileDrag();
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? (e.AllowedEffects&DragDropEffects.Link)!=0?DragDropEffects.Link:e.AllowedEffects&DragDropEffects.Copy : DragDropEffects.None;
        if (e.Data.GetData(DragFormat) is string id)
        {
            var pos = e.GetPosition(strip);
            if (state.FavoriteIds.Contains(id))
            {
                TrackFavoritePreview(id,pos);
                e.Effects = favoriteDestination>=0 ? DragDropEffects.Move : DragDropEffects.None;
                e.Handled = true;
                return;
            }
            var target = TileDropTarget(id, pos);
            e.Effects = target is null ? DragDropEffects.None : DragDropEffects.Move;
            ShowDropMarker(id, target);
            if (pos.X >= FixedSpan && pos.X <= Viewport && pos.Y is >= 60 and <= 176)
            {
                if (pos.X < FixedSpan + 24) ScrollBy(-12);
                else if (pos.X > Viewport - 24) ScrollBy(12);
            }
        }
        e.Handled = true;
    }

    void OnDrop(object sender, DragEventArgs e)
    {
        var source=e.OriginalSource as DependencyObject;
        if (IsOrganizerButton(source) && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            EndFileDrag();
            if(!state.Files.QuickDropCollapsed)OpenDesktopOrganizer();
            e.Effects=DragDropEffects.None;
            e.Handled=true;
            return;
        }
        if(IsFileSurface(source)||IsFilesButton(source)){HandleFileDrop(sender,e);return;}
        bool cancelledQuickDrop=quickDropTemporary&&e.Data.GetDataPresent(DataFormats.FileDrop);
        EndFileDrag();
        if(cancelledQuickDrop){e.Effects=DragDropEffects.None;e.Handled=true;return;}
        if (e.Data.GetData(DragFormat) is string id)
        {
            var pos=e.GetPosition(strip);
            if (state.FavoriteIds.Contains(id))
                e.Effects = CommitFavoriteDrop(id,pos) ? DragDropEffects.Move : DragDropEffects.None;
            else
            {
                var target = TileDropTarget(id,pos);
                e.Effects = target is not null && ReorderTile(id,target.Item.Id) ? DragDropEffects.Move : DragDropEffects.None;
            }
            dropMarker.Visibility = Visibility.Collapsed;
        }
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths){AddPaths(paths);e.Effects=(e.AllowedEffects&DragDropEffects.Link)!=0?DragDropEffects.Link:e.AllowedEffects&DragDropEffects.Copy;}
        e.Handled = true;
    }

    void Place(bool top)
    {
        var area = NativeServices.WorkingArea(this); state.TopEdge = top;
        var position=DockLayout.FloatingPosition(new(area.Left,area.Top,area.Width,area.Height),Width,Height,top);Left=position.X;Top=position.Y;
        hovering = false; nameBubble.Visibility = Visibility.Collapsed; Layout(); Save();
    }

    void KeepVisible()
    {
        var area = NativeServices.WorkingArea(this); Width = Math.Min(Width, Math.Max(300, area.Width - 24));
        var pos = DockMath.KeepVisible(Left, Top, Width, Height, area.Left, area.Top, area.Width, area.Height);
        Left = pos.X; Top = pos.Y;
    }

    void DockWorkAreaChanged(object sender,UserPreferenceChangedEventArgs e)=>DisplayChanged(sender,e);
    void DisplayChanged(object? sender, EventArgs e) { if (loaded) Dispatcher.BeginInvoke(() => { KeepVisible(); Layout();PositionDesktopCards();PositionWeatherCard();PositionMusicCard(); Save(); }); }

    void Save()
    {
        if (!loaded) return;
        state.X = Left; state.Y = Top;
        var pixels = NativeServices.PixelPosition(this); state.PixelX = pixels.X; state.PixelY = pixels.Y;
        try { DockStore.Save(statePath, state); savingError = false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!savingError) { savingError = true; MessageBox.Show(this, "设置暂时无法保存，请将程序放在可写入的文件夹。\n" + ex.Message, "悬浮控制中心", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    void Try(Action action)
    {
        try { action(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法完成操作", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}

