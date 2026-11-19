using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Net.Http;
using System.Net;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Effects;
using Rectangle=System.Windows.Shapes.Rectangle;
using FloatingDock;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
        var app = new Application();
        int failure = 0;
        app.DispatcherUnhandledException += (_, e) => { Console.WriteLine("FAIL UI dispatcher: " + e.Exception); failure++; e.Handled = true; app.Shutdown(); };
        var state = new DockState{AutoImportDesktop=false}; DockCatalog.Merge(state, NativeServices.DesktopShortcuts().Take(32));
        state.Width=1180;
        state.CollectionPath=Path.Combine(Directory.GetCurrentDirectory(),"work","ui-test-collection-"+Guid.NewGuid());
        Directory.CreateDirectory(state.CollectionPath);
        foreach(var name in new[]{"文档","图片","毕业论文","mc","游戏资料","游戏入口","PikPak","隐藏图标","桌面快捷方式"})Directory.CreateDirectory(Path.Combine(state.CollectionPath,name));
        if (state.Items.Count == 0) { Console.WriteLine("FAIL fixture: no desktop shortcuts"); return 1; }
        // Construct before Application.Run: async completions must marshal to this window's Dispatcher.
        var weatherHandler=new WeatherFixture();var musicFixture=new MusicFixtureState();
        var service=args.Contains("--live-weather")?null:new WeatherService(new HttpClient(weatherHandler));
        var window = new DockWindow(state, Path.Combine(Directory.GetCurrentDirectory(), "work", "ui-test-data", "settings.json"),service,()=>new MusicFixtureSession(musicFixture));
        if (args.Contains("--interactive")) { window.ShowInTaskbar = true; window.ShowActivated = true; app.Run(window); return 0; }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            try
            {
                var widgetFlags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var weather=typeof(DockWindow).GetField("weatherCard",widgetFlags)?.GetValue(window) as Window;
                if(weather is null||!weather.IsVisible)throw new Exception("independent weather widget missing");
                var siblings=Application.Current.Windows.OfType<DesktopFileCard>().Select(c=>(c.Left,c.Top,c.Width,c.Height,c.IsVisible)).ToArray();
                var closeWeather=Descendants(weather).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="隐藏天气组件");
                closeWeather.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(weather.IsVisible||!siblings.SequenceEqual(Application.Current.Windows.OfType<DesktopFileCard>().Select(c=>(c.Left,c.Top,c.Width,c.Height,c.IsVisible))))throw new Exception("weather hide affected file widgets");
                var widgetMenu=(ContextMenu)typeof(DockWindow).GetMethod("BuildMenu",widgetFlags)!.Invoke(window,null)!;
                MenuPath(widgetMenu,"更多","桌面组件","显示天气组件").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(!weather.IsVisible)throw new Exception("weather restore failed");
                Console.WriteLine("PASS weather hides and restores independently of file cards");

                var music=(DesktopMusicCard)typeof(DockWindow).GetField("musicCard",widgetFlags)!.GetValue(window)!;
                if(music is null||!music.IsVisible||!Descendants(music).OfType<TextBlock>().Any(t=>t.Text=="音乐组件测试"))throw new Exception("music widget did not show injected current song");
                var applyMusic=typeof(DesktopMusicCard).GetMethod("Apply",widgetFlags)!;
                var musicProgress=Descendants(music).OfType<Slider>().Single(s=>s.Name=="MusicProgress");
                applyMusic.Invoke(music,new object[]{MusicSnapshot.Parse("""{"Available":true,"Source":"cloudmusic.exe","Title":"真实歌曲","Artist":"真实歌手","Album":"真实专辑","State":"Paused","CanSeek":true,"Duration":0}""")});
                if(musicProgress.Visibility!=Visibility.Collapsed||musicProgress.IsEnabled||!Descendants(music).OfType<TextBlock>().Any(t=>t.Text=="客户端未提供进度"))throw new Exception("missing timeline still looks like zero length or allows seeking");
                if(!Descendants(music).OfType<TextBlock>().Any(t=>t.Text.Contains("真实歌手")&&t.Text.Contains("真实专辑")&&(t.ToolTip as string)?.Contains("真实专辑")==true))throw new Exception("album and artist details missing");
                var unavailableSnapshot=args.FirstOrDefault(a=>a.StartsWith("--snapshot="));
                if(unavailableSnapshot is not null){music.UpdateLayout();Capture(music,Path.ChangeExtension(unavailableSnapshot[11..],null)+"-music-unavailable.png");}
                applyMusic.Invoke(music,new object[]{new MusicSnapshot()});
                if(Descendants(music).OfType<TextBlock>().Any(t=>t.Text.Contains("真实专辑")||t.Text.Contains("真实歌曲")))throw new Exception("disconnected music retains stale metadata");
                applyMusic.Invoke(music,new object[]{await new MusicFixtureSession(musicFixture).ReadAsync(CancellationToken.None)});
                if(musicProgress.Visibility!=Visibility.Visible||!musicProgress.IsEnabled||musicProgress.Maximum!=240||!Descendants(music).OfType<TextBlock>().Any(t=>t.Text=="1:15 / 4:00"))throw new Exception("available timeline failed to recover after missing data");
                Console.WriteLine("PASS music album details unknown timeline disconnect clearing and known timeline recovery");
                var unplacedMusic=new DesktopMusicCard(()=>new MusicFixtureSession(musicFixture),()=>{},()=>{},()=>{});
                try{var setting=new DesktopCardPlacement{Width=304,Height=216};unplacedMusic.ConfigurePlacement(setting,()=>{});unplacedMusic.RestoreOrPlace(new(100,100,304,216),true);if(unplacedMusic.Height!=48||setting.Height!=216)throw new Exception("collapsed music without saved coordinates uses expanded empty height");}finally{unplacedMusic.CloseForShutdown();}
                var musicRect=(music.Left,music.Top,music.Width,music.Height);var musicSiblingRects=app.Windows.OfType<Window>().Where(w=>w==weather||w is DesktopFileCard).Select(w=>(w.Left,w.Top,w.Width,w.Height,w.IsVisible)).ToArray();
                Descendants(music).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="播放或暂停").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                if(!musicFixture.Commands.Contains("toggle")||!Descendants(music).OfType<TextBlock>().Any(t=>t.Text=="正在播放"))throw new Exception("music transport did not control selected provider");
                Descendants(music).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="隐藏网易云播放器").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(music.IsVisible||musicFixture.Disposals<1||!musicSiblingRects.SequenceEqual(app.Windows.OfType<Window>().Where(w=>w==weather||w is DesktopFileCard).Select(w=>(w.Left,w.Top,w.Width,w.Height,w.IsVisible))))throw new Exception("music hide changed siblings or leaked session");
                var musicMenu=(ContextMenu)typeof(DockWindow).GetMethod("BuildMenu",widgetFlags)!.Invoke(window,null)!;MenuPath(musicMenu,"更多","桌面组件","显示网易云播放器").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(100);
                if(!music.IsVisible||Math.Abs(musicRect.Left-music.Left)>1||Math.Abs(musicRect.Top-music.Top)>1||Math.Abs(musicRect.Width-music.Width)>.1||Math.Abs(musicRect.Height-music.Height)>.1)throw new Exception($"music restore lost geometry: before={musicRect}, after={(music.Left,music.Top,music.Width,music.Height)}, visible={music.IsVisible}");
                void MusicGesture(string name,double dx,double dy){var h=Descendants(music).OfType<System.Windows.Controls.Primitives.Thumb>().Single(t=>t.Name==name);h.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});h.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(dx,dy){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});h.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(dx,dy,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});}
                MusicGesture("CardMove",-10000,-10000);var musicSafe=NativeServices.WorkingArea(music);if(music.Left<musicSafe.Left||music.Top<musicSafe.Top)throw new Exception("music entered taskbar");MusicGesture("CardResizeRight",32,0);if(Math.Abs(music.Width-musicRect.Width-32)>.1)throw new Exception("music resize failed");
                MusicGesture("CardMove",musicRect.Left-music.Left,musicRect.Top-music.Top);MusicGesture("CardResizeRight",musicRect.Width-music.Width,0);
                Console.WriteLine("PASS music title transport independent hide restore safe movement resize and service disposal");

                var dockHeight=window.Height;var dockTop=window.Top;var space=NativeServices.WorkingArea(window);
                window.Top=space.Top;window.Height=space.Height;state.WeatherWidget.Placement.PixelX=state.WeatherWidget.Placement.PixelY=null;weather.Hide();
                typeof(DockWindow).GetMethod("PositionWeatherCard",widgetFlags)!.Invoke(window,null);if(weather.IsVisible)throw new Exception("insufficient space did not suppress pending weather");
                window.Height=dockHeight;window.Top=dockTop;await Task.Delay(100);
                if(!weather.IsVisible)throw new Exception("weather never recovers after workspace becomes available");
                Console.WriteLine("PASS pending weather placement recovers when workspace geometry changes");
                void WeatherGesture(string name,double dx,double dy){var h=Descendants(weather).OfType<System.Windows.Controls.Primitives.Thumb>().Single(t=>t.Name==name);h.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});h.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(dx,dy){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});h.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(dx,dy,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});}
                var wx=weather.Left;WeatherGesture("CardMove",25,20);if(weather.Left<=wx)throw new Exception("weather title drag failed");var ww=weather.Width;WeatherGesture("CardResizeRight",30,0);if(weather.Width!=ww+30)throw new Exception("weather resize failed");
                WeatherGesture("CardMove",-10000,-10000);var safeWeather=NativeServices.WorkingArea(weather);if(weather.Left<safeWeather.Left||weather.Top<safeWeather.Top)throw new Exception("weather entered taskbar");
                var expanded=weather.Height;Descendants(weather).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="折叠或展开天气组件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));WeatherGesture("CardMove",20,20);if(weather.Height!=48||state.WeatherWidget.Placement.Height!=expanded)throw new Exception("collapsed weather overwrote expanded height");
                Descendants(weather).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="折叠或展开天气组件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));if(Math.Abs(weather.Height-expanded)>.1)throw new Exception($"weather expand failed: expanded={expanded}, actual={weather.Height}, saved={state.WeatherWidget.Placement.Height}, auto={state.WeatherWidget.Placement.AutoCollapsed}, collapsed={state.WeatherWidget.Collapsed}, dock={window.Top}/{window.Height}");
                var restoredWeather=new DesktopWeatherCard(()=>{},()=>{},_=>{});try{restoredWeather.ConfigurePlacement(state.WeatherWidget.Placement,()=>{});restoredWeather.RestoreOrPlace(new(700,200,380,392),false);var point=NativeServices.PixelPosition(restoredWeather);if(Math.Abs(point.X-state.WeatherWidget.Placement.PixelX!.Value)>2||restoredWeather.Width!=weather.Width)throw new Exception("weather restart geometry failed");}finally{restoredWeather.CloseForShutdown();}
                WeatherGesture("CardMove",wx-weather.Left,120-weather.Top);WeatherGesture("CardResizeRight",ww-weather.Width,0);
                Console.WriteLine("PASS weather move resize taskbar constraint collapse and new-window restore");
                var images = Descendants(window).OfType<Image>().Where(i => i.Source is not null).ToList();
                if (images.Count < Math.Min(5, state.Items.Count)) throw new Exception($"only {images.Count} icons loaded");
                if (images.Any(i => i.Source is Freezable f && !f.IsFrozen)) throw new Exception("cross-thread images not frozen");
                Console.WriteLine($"PASS async icons constructed before dispatcher: {images.Count} real icons");
                if (window.Topmost || window.ShowInTaskbar) throw new Exception("desktop layer must not be topmost");
                var cover=new Window{Title="Dock layer test",Width=200,Height=100,Left=window.Left,Top=window.Top,ShowInTaskbar=false};
                try {
                    cover.Show();window.Activate();await Task.Delay(150);
                    if(!IsAbove(new WindowInteropHelper(cover).Handle,new WindowInteropHelper(window).Handle))throw new Exception("Dock covers normal app after activation");
                    Console.WriteLine("PASS normal application stays above Dock even after Dock activation");
                    var desktopSurfaces=app.Windows.OfType<Window>().Where(w=>w==window||w is DesktopFileCard||w is DesktopWeatherCard||w is DesktopMusicCard).ToArray();
                    // Reproduce a Shell window that changes Z order after the cards were placed.
                    SetWindowPos(new WindowInteropHelper(cover).Handle,new WindowInteropHelper(weather).Handle,0,0,0,0,0x13);
                    var layerDeadline=DateTime.UtcNow.AddSeconds(2);
                    while(desktopSurfaces.Any(w=>!IsAbove(new WindowInteropHelper(cover).Handle,new WindowInteropHelper(w).Handle))&&DateTime.UtcNow<layerDeadline)await Task.Delay(40);
                    if(desktopSurfaces.Any(w=>!IsAbove(new WindowInteropHelper(cover).Handle,new WindowInteropHelper(w).Handle)))throw new Exception("desktop cards remain above a normal window after its Z order changes");
                    foreach(var desktopSurface in desktopSurfaces){desktopSurface.Activate();await Task.Delay(50);if(!IsAbove(new WindowInteropHelper(cover).Handle,new WindowInteropHelper(desktopSurface).Handle))throw new Exception("card activation raised it over normal application");}
                    Console.WriteLine("PASS all desktop surfaces stay below normal windows after external reorder and activation");

                    cover.Activate();
                    await Task.Delay(100);
                    var coverHandle=new WindowInteropHelper(cover).Handle;
                    var dockHwnd=new WindowInteropHelper(window).Handle;
                    var activeBefore=GetForegroundWindow();
                    var toggleShortcut=typeof(DockWindow).GetMethod("ToggleFromShortcut",widgetFlags)!;
                    toggleShortcut.Invoke(window,null);
                    await Task.Delay(120);
                    if(!IsAbove(dockHwnd,coverHandle)||GetForegroundWindow()!=activeBefore
                        ||desktopSurfaces.Any(w=>w!=window&&!IsAbove(coverHandle,new WindowInteropHelper(w).Handle)))
                        throw new Exception("shortcut must raise only the Dock while preserving the active window");
                    Console.WriteLine("PASS shortcut raises only the Dock without taking focus");

                    var launchFromDock=typeof(DockWindow).GetMethod("LaunchFromDock",widgetFlags)!;
                    var rejectedLaunch=new InvalidOperationException("fixture launch error");
                    try
                    {
                        launchFromDock.Invoke(window,new object[]{(Action)(()=>throw rejectedLaunch)});
                        throw new Exception("failed launch was swallowed");
                    }
                    catch(System.Reflection.TargetInvocationException e) when(e.InnerException==rejectedLaunch) { }
                    if(!IsAbove(dockHwnd,coverHandle))throw new Exception("failed launch dismissed the Dock");
                    launchFromDock.Invoke(window,new object[]
                    {
                        (Action)(()=>{if(!IsAbove(dockHwnd,coverHandle))throw new Exception("Dock yielded before dispatching the launch");})
                    });
                    if(!IsAbove(coverHandle,dockHwnd)||!window.IsVisible||state.DockCollapsed||GetForegroundWindow()!=activeBefore)
                        throw new Exception("successful launch handoff did not restore the desktop layer in place");
                    Console.WriteLine("PASS launch handoff returns the Dock to the desktop layer only after a successful request");

                    var dragging=typeof(DockWindow).GetField("internalDrag",widgetFlags)!;
                    dragging.SetValue(window,true);
                    try
                    {
                        toggleShortcut.Invoke(window,null);
                        if(state.DockCollapsed)throw new Exception("shortcut interrupted an active icon drag");
                    }
                    finally { dragging.SetValue(window,false); }

                    toggleShortcut.Invoke(window,null);
                    await Task.Delay(340);
                    var edgeHandle=app.Windows.OfType<Window>().Single(w=>w.Title=="展开 Dock");
                    if(!state.DockCollapsed||window.IsVisible||!edgeHandle.IsVisible||GetForegroundWindow()!=activeBefore)
                        throw new Exception("the second shortcut after a launch handoff did not just close the Dock");
                    if(SystemParameters.ClientAreaAnimation)
                    {
                        toggleShortcut.Invoke(window,null);
                        var movingDock=app.Windows.OfType<Window>().Single(w=>w.Title=="Dock 边缘动效");
                        await Task.Delay(65);
                        if(!movingDock.IsVisible)throw new Exception("edge motion finished before the intended reversal");
                        toggleShortcut.Invoke(window,null);
                        if(!state.DockCollapsed||!movingDock.IsVisible)
                            throw new Exception("a second shortcut during recall did not reuse the moving surface");
                        await Task.Delay(340);
                        if(window.IsVisible||!edgeHandle.IsVisible||movingDock.IsVisible||GetForegroundWindow()!=activeBefore)
                            throw new Exception("shortcut reversal left the Dock, handle or focus out of sync");
                    }
                    ((Button)edgeHandle.Content).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(340);
                    if(!window.IsVisible||edgeHandle.IsVisible||!IsAbove(coverHandle,dockHwnd))
                        throw new Exception("manual reveal did not return to the usual desktop layer");
                    Console.WriteLine("PASS second shortcut collapses after the handoff and reverses an unfinished recall");
                }finally{cover.Close();}
                var toolbar=Descendants(window).OfType<Border>().FirstOrDefault(b=>b.Name=="ToolbarBackground");
                var islands=Descendants(window).OfType<Border>().Where(b=>b.Name is "InfoIsland" or "ToolbarBackground" or "ToolsIsland").OrderBy(Canvas.GetLeft).ToArray();
                if(islands.Count(b=>b.Visibility==Visibility.Visible)!=1||toolbar!.BorderThickness!=new Thickness(0))throw new Exception("single borderless launcher surface required");
                var mirrors=Descendants(window).OfType<Border>().Where(b=>b.Name is "IconReflection" or "SystemIconReflection" or "CategoryIconReflection").ToArray();if(mirrors.Length!=state.Items.Count+4||mirrors.Any(m=>m.IsHitTestVisible||m.Opacity>.25||m.Background is not VisualBrush||m.RenderTransform is not ScaleTransform flip||flip.ScaleY!=-1))throw new Exception("reflections must be subtle, mirrored and noninteractive");
                Console.WriteLine("PASS single launcher surface and noninteractive faded icon reflections");
                if(toolbar?.Background is not SolidColorBrush fill||fill.Color.A==0||fill.Color.A>80||toolbar.ActualHeight<36)throw new Exception("glass toolbar fallback must remain translucent");
                Console.WriteLine("PASS glass toolbar has a translucent fallback");
                var acrylic=Descendants(toolbar).OfType<AcrylicMaterial>().SingleOrDefault()??throw new Exception("acrylic missing");
                var wallpaper=Descendants(acrylic).OfType<Rectangle>().First(r=>r.Name=="AcrylicWallpaper");
                if(wallpaper.Effect is not BlurEffect blur||blur.Radius<10)throw new Exception("frosted blur missing");
                if(!Descendants(acrylic).OfType<Rectangle>().Any(r=>r.Name=="AcrylicGrain"&&r.Fill is DrawingBrush))throw new Exception("grain missing");
                if(acrylic.Clip is not RectangleGeometry clip||clip.Rect.Height>toolbar.ActualHeight)throw new Exception("acrylic is not clipped to toolbar");
                Console.WriteLine("PASS acrylic blur grain and rounded toolbar clipping");
                Console.WriteLine(wallpaper.Fill is ImageBrush?"PASS actual desktop wallpaper loaded":"INFO wallpaper fallback active");
                if(wallpaper.Fill is not ImageBrush) {
                    var provider=typeof(App).Assembly.GetType("FloatingDock.DesktopWallpaper")!;
                    var entries=((System.Collections.IEnumerable)provider.GetMethod("Read")!.Invoke(null,null)!).Cast<object>().ToArray();
                    Console.WriteLine($"INFO wallpaper descriptors: {entries.Length}; readable image files: {entries.Count(d=>File.Exists((string?)d.GetType().GetProperty("Path")!.GetValue(d)))}");
                }
                var pixels = NativeServices.PixelPosition(window);
                double? originalViewport=(wallpaper.Fill as ImageBrush)?.Viewport.X;
                window.Left += 30;await Task.Delay(100);
                if(originalViewport is double previousX&&wallpaper.Fill is ImageBrush movedBrush&&Math.Abs(movedBrush.Viewport.X-previousX+30)>2)throw new Exception("wallpaper sample did not follow window move");
                NativeServices.RestorePixelPosition(window,pixels.X,pixels.Y);
                var restored = NativeServices.PixelPosition(window);
                if (Math.Abs(restored.X-pixels.X)>2 || Math.Abs(restored.Y-pixels.Y)>2) throw new Exception("physical position restore failed");
                Console.WriteLine("PASS physical window position restore");
                var buttons = Descendants(window).OfType<Button>().ToList();
                foreach(var label in new[]{"添加快捷方式","控制中心菜单","天气与城市","拖动这里移动控制中心"}){
                    var control=buttons.First(b=>AutomationProperties.GetName(b)==label);
                    if(control.Visibility!=Visibility.Visible)continue;
                    if(!islands.Any(b=>Canvas.GetLeft(control)>=Canvas.GetLeft(b)&&Canvas.GetLeft(control)+control.ActualWidth<=Canvas.GetLeft(b)+b.ActualWidth+1&&Canvas.GetTop(control)>=Canvas.GetTop(b)&&Canvas.GetTop(control)+control.ActualHeight<=Canvas.GetTop(b)+b.ActualHeight+1))throw new Exception("control outside all islands: "+label);
                }
                foreach(var label in new[]{"此电脑","回收站","控制面板","文件收纳"})if(!buttons.Any(b=>AutomationProperties.GetName(b)==label))throw new Exception("missing integrated entry: "+label);
                foreach(var label in new[]{"此电脑","回收站","控制面板","文件收纳"}){
                    var button=buttons.First(b=>AutomationProperties.GetName(b)==label&&(b.Name.StartsWith("SystemShortcut")||b.Name=="FilesShortcut"));
                    if(button.Name.StartsWith("SystemShortcut")){
                        if(button.Content is not TextBlock glyph||!glyph.FontFamily.Source.Contains("Segoe Fluent")||button.Width!=56||button.Height!=56)throw new Exception("system plate must match app icon dimensions: "+label);
                    }else{
                        if(button.Content is not Image artwork||artwork.Source is not Freezable source||!source.IsFrozen)throw new Exception("file artwork not loaded: "+label);
                    }
                }
                if(!Descendants(weather).OfType<Image>().Any(i=>i.Source is DrawingImage))throw new Exception("colored weather artwork not loaded");
                Console.WriteLine("PASS consistent system icon plates and colored weather artwork");
                foreach(var code in new[]{0,1,2,3,45,48,51,53,55,56,57,61,63,65,66,67,71,73,75,77,80,81,82,85,86,95,96,99})foreach(var day in new[]{false,true}){
                    var source=WeatherArtwork.For(code,day);if(!source.IsFrozen||source.Width!=32||source.Height!=32)throw new Exception("weather artwork bounds invalid: "+code);
                }
                var variants=new[]{WeatherArtwork.For(0,true),WeatherArtwork.For(0,false),WeatherArtwork.For(3,true),WeatherArtwork.For(45,true),WeatherArtwork.For(61,true),WeatherArtwork.For(75,true),WeatherArtwork.For(95,true)};
                if(variants.Select(ArtworkHash).Distinct().Count()!=variants.Length)throw new Exception("different weather conditions rendered identical artwork");
                Console.WriteLine("PASS all 28 weather codes render; day night rain snow fog thunder remain distinct");
                var artworkSnapshot=args.FirstOrDefault(a=>a.StartsWith("--snapshot="));
                if(artworkSnapshot is not null)CaptureArtworkSheet(Path.ChangeExtension(artworkSnapshot[11..],null)+"-artwork.png");
                foreach(var label in new[]{"此电脑","回收站","控制面板"})if(!buttons.Single(b=>b.Name.StartsWith("SystemShortcut")&&AutomationProperties.GetName(b)==label).IsVisible)throw new Exception("system entry hidden: "+label);
                Console.WriteLine("PASS fixed system shortcuts visible at full width");
                var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                var dc=(DesktopFileCard)typeof(DockWindow).GetField("downloadCard",flags)!.GetValue(window)!;
                var fc=(DesktopFileCard)typeof(DockWindow).GetField("folderCard",flags)!.GetValue(window)!;
                var fcRect=(fc.Left,fc.Top,fc.Width,fc.Height);
                Descendants(dc).OfType<Button>().Single(b=>b.Content as string=="×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(dc.IsVisible||!fc.IsVisible)throw new Exception("closing download card also hid folder card");
                var cm=(ContextMenu)typeof(DockWindow).GetMethod("BuildMenu",flags)!.Invoke(window,null)!;
                AssertDailyMenu(cm);
                MenuPath(cm,"更多","桌面组件","显示下载收件箱").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(!dc.IsVisible||!fc.IsVisible||fcRect!=(fc.Left,fc.Top,fc.Width,fc.Height))throw new Exception("independent restore moved sibling");
                Descendants(fc).OfType<Button>().Single(b=>b.Content as string=="×").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!dc.IsVisible||fc.IsVisible)throw new Exception("closing folder card also hid download card");
                cm=(ContextMenu)typeof(DockWindow).GetMethod("BuildMenu",flags)!.Invoke(window,null)!;
                MenuPath(cm,"更多","桌面组件","显示常用文件夹").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(!dc.IsVisible||!fc.IsVisible)throw new Exception("folder restore failed");
                Console.WriteLine("PASS cards hide and restore independently without moving sibling");
                Console.WriteLine("PASS integrated one-row controls weather and system/file entries");
                var filesButton=buttons.First(b=>AutomationProperties.GetName(b)=="文件收纳");
                filesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
                var popup=(System.Windows.Controls.Primitives.Popup)typeof(DockWindow).GetField("collectionPopup",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                if(!popup.IsOpen)throw new Exception("file flyout closed immediately");
                Button Find(string name)=>Descendants(popup.Child).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==name);
                Button[] Rows()=>Descendants(popup.Child).OfType<Button>().Where(b=>b.Tag is BrowserEntry).ToArray();
                var search=Descendants(popup.Child).OfType<TextBox>().Single();
                if(!search.IsVisible)throw new Exception("file search must be visible without another click");
                if(!state.Files.SelectedSource.Equals("recent"))throw new Exception("first file view must show recent files");
                if(AutomationProperties.GetItemStatus(Find("最近文件"))!="当前来源")
                    throw new Exception("the selected source is missing its accessible selection indicator");
                Find("收纳").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(1000);
                if(AutomationProperties.GetItemStatus(Find("收纳"))!="当前来源"||
                    AutomationProperties.GetItemStatus(Find("最近文件"))=="当前来源")
                    throw new Exception("source selection did not move with file navigation");
                Console.WriteLine("PASS file source selection and its accessible label follow navigation");
                if(Rows().Length!=9)throw new Exception($"expected nine collection folders, got {Rows().Length}");
                if(Descendants(popup.Child).OfType<TextBlock>().Any(t=>t.Text.Any(c=>c>=0x4E00&&c<=0x9FFF)&&t.FontFamily.Source.Contains("Fluent")))throw new Exception("Chinese file labels inherited icon font");
                search.Text="论文";
                var searchDeadline=DateTime.UtcNow.AddSeconds(3);while(Rows().Length!=1&&DateTime.UtcNow<searchDeadline)await Task.Delay(50);
                if(Rows().Length!=1)throw new Exception("recursive Chinese file filtering failed: count="+Rows().Length+", status="+Descendants(popup.Child).OfType<TextBlock>().Last().Text+", names="+string.Join(",",Rows().Select(b=>((BrowserEntry)b.Tag).Name)));
                search.Text="";await Task.Delay(700);
                var paperFolder=Rows().Single(b=>((BrowserEntry)b.Tag).Name=="毕业论文");
                paperFolder.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!Rows().Any(b=>((BrowserEntry)b.Tag).Name=="毕业论文"))throw new Exception("single click must select without opening");
                paperFolder.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Control.MouseDoubleClickEvent});await Task.Delay(700);
                if(Rows().Length!=0)throw new Exception("folder click did not navigate inside panel");
                var created=Path.Combine(state.CollectionPath,"毕业论文","刚新建的报告.txt");File.WriteAllText(created,"UI fixture");
                Find("刷新文件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                var file=Rows().Single(b=>((BrowserEntry)b.Tag).Name=="刚新建的报告.txt");
                if(file.ContextMenu is null||file.ContextMenu.Items.OfType<MenuItem>().Count()<3)throw new Exception("file actions missing");
                if(!(file.ToolTip?.ToString()?.Contains("毕业论文")??false))throw new Exception("file source missing");
                Find("上一级").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                if(Rows().Length!=9)throw new Exception("up navigation did not restore collection root");
                var added=Path.Combine(state.CollectionPath,"刚下载完成.zip");File.WriteAllText(added,"UI fixture");
                var refreshDeadline=DateTime.UtcNow.AddSeconds(3);while(!Rows().Any(b=>((BrowserEntry)b.Tag).Name=="刚下载完成.zip")&&DateTime.UtcNow<refreshDeadline)await Task.Delay(50);
                if(!Rows().Any(b=>((BrowserEntry)b.Tag).Name=="刚下载完成.zip"))throw new Exception("open file panel did not automatically refresh new files: popup="+popup.IsOpen+", busy="+typeof(DockWindow).GetField("fileQueryBusy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window));
                Find("下载").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(Rows().Length!=0)throw new Exception("navigation leaves previous directory files clickable while new location is loading");
                await Task.Delay(50);
                if(!Descendants(popup.Child).OfType<TextBlock>().Any(t=>Equals(t.ToolTip,FileLocations.Downloads())))throw new Exception("downloads did not navigate to resolved known folder");
                Find("桌面收件箱").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);
                if(!Descendants(popup.Child).OfType<TextBlock>().Any(t=>Equals(t.ToolTip,Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))))throw new Exception("desktop did not navigate inside panel");
                Find("收纳").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                var dropSources=Path.Combine(state.CollectionPath,"拖放测试源");Directory.CreateDirectory(dropSources);
                var moveSource=Path.Combine(dropSources,"拖入的文件.txt");File.WriteAllText(moveSource,"drag route data");
                var dropFolder=Rows().Single(b=>((BrowserEntry)b.Tag).Name=="毕业论文");
                using(var shellData=ShellDragData.ForFiles([moveSource]))
                {
                    var beforeDragRows=Rows();
                    File.WriteAllText(Path.Combine(state.CollectionPath,"刷新不应打断拖动.txt"),"refresh during drag");
                    Find("刷新文件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var over=RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    if(over.Effects!=DragDropEffects.Move)throw new Exception("file row did not negotiate native move");
                    await Task.Delay(500);
                    if(!beforeDragRows.SequenceEqual(Rows()))throw new Exception("in-flight query replaced file targets while dragging");
                    RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.None,DragDrop.PreviewDropEvent);
                }
                var moved=Path.Combine(state.CollectionPath,"毕业论文","拖入的文件.txt");
                if(File.Exists(moveSource)||!File.Exists(moved)||File.ReadAllText(moved)!="drag route data")throw new Exception("folder row drop failed to move real temporary file");
                await Task.Delay(500);
                var linkSource=Path.Combine(dropSources,"保留原位置.txt");File.WriteAllText(linkSource,"link source");
                dropFolder=Rows().Single(b=>((BrowserEntry)b.Tag).Name=="毕业论文");
                using(var shellData=ShellDragData.ForFiles([linkSource]))
                {
                    var paths=(string[]?)shellData.Data.GetData(DataFormats.FileDrop)??[];
                    if(paths.Length!=1||paths[0]!=linkSource)throw new Exception("outgoing Shell data lost original file path");
                    var over=RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.LeftMouseButton|DragDropKeyStates.AltKey,DragDrop.PreviewDragOverEvent);
                    if(over.Effects!=DragDropEffects.Link)throw new Exception("Alt drag did not negotiate shortcut creation");
                    RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.AltKey,DragDrop.PreviewDropEvent);
                }
                if(!File.Exists(linkSource)||!Directory.EnumerateFiles(Path.Combine(state.CollectionPath,"毕业论文"),"*.lnk").Any())throw new Exception("Alt drop did not create shortcut while keeping source");
                await Task.Delay(500);
                dropFolder=Rows().Single(b=>((BrowserEntry)b.Tag).Name=="毕业论文");
                using(var shellData=ShellDragData.ForFiles([linkSource]))
                {
                    RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    RaiseDrag(dropFolder,shellData.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                    await Task.Delay(300);
                    if((bool)typeof(DockWindow).GetField("fileDragActive",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!)throw new Exception("cancelled incoming drag leaves refresh and closing locked");
                }
                var quickProject=Directory.CreateDirectory(Path.Combine(dropSources,"项目归类")).FullName;
                state.Files.ProjectDropFolder=quickProject;
                var quickSource=Path.Combine(dropSources,"投放验证.txt");
                File.WriteAllText(quickSource,"quick-drop data");
                var organizer=Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="桌面分类与整理");
                organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
                var projectZone=Find("投放到项目");
                var restingBorder=projectZone.BorderBrush.ToString();
                using(var shellData=ShellDragData.ForFiles([quickSource]))
                {
                    RaiseDrag(projectZone,shellData.Data,DragDropKeyStates.None,DragDrop.PreviewDropEvent);
                    if(!File.Exists(quickSource))throw new Exception("drop without a destination preview moved the file");
                    if(!popup.IsOpen){organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);}
                    var over=RaiseDrag(projectZone,shellData.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    var pathHint=Descendants(popup.Child).OfType<TextBlock>().Single(t=>t.Name=="QuickDropFeedback");
                    if(over.Effects!=DragDropEffects.Move||!pathHint.Text.Contains(quickProject)||projectZone.BorderBrush.ToString()==restingBorder)
                        throw new Exception($"quick-drop preview: open={popup.IsOpen}, visible={projectZone.IsVisible}, effect={over.Effects}, body={pathHint.Text}, configured={quickProject}, border={projectZone.BorderBrush}, baseline={restingBorder}");
                    if(!File.Exists(quickSource))throw new Exception("drag-over moved the source");
                    RaiseDrag(projectZone,shellData.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                    await Task.Delay(200);
                    if(projectZone.BorderBrush.ToString()!=restingBorder||pathHint.Text.Contains(quickProject))throw new Exception("cancel did not reset quick drop feedback");
                    if(!popup.IsOpen){organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);}
                    RaiseDrag(projectZone,shellData.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    RaiseDrag(projectZone,shellData.Data,DragDropKeyStates.None,DragDrop.PreviewDropEvent);
                }
                if(File.Exists(quickSource)||File.ReadAllText(Path.Combine(quickProject,"投放验证.txt"))!="quick-drop data")
                    throw new Exception("quick drop did not reach the shown target");
                if(!popup.IsOpen){organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(150);}
                var toggleQuickDrop=Find("快速归类");
                toggleQuickDrop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(100);
                var dockConfig=typeof(DockWindow).GetField("statePath",widgetFlags)!.GetValue(window) as string;
                if(projectZone.IsVisible||!DockStore.Load(dockConfig!).Files.QuickDropCollapsed)
                    throw new Exception("collapse was not applied and persisted for quick drop");
                toggleQuickDrop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(!projectZone.IsVisible)throw new Exception("reopening quick drop did not show targets");
                Find("收纳").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
                if(projectZone.IsVisible)throw new Exception("quick drop should only appear for desktop");
                Console.WriteLine("PASS quick-drop exact path highlight, cancel feedback, native drop and persisted collapse");
                // A folded panel can spring open for one incoming drag without changing saved preferences.
                state.Files.QuickDropCollapsed=true;
                popup.IsOpen=false;
                var springSource=Path.Combine(dropSources,"临时投放.txt");
                File.WriteAllText(springSource,"spring-drop data");
                var originalFileSource=state.Files.SelectedSource;
                var originalPopupTarget=popup.PlacementTarget;
                using(var incoming=ShellDragData.ForFiles([springSource]))
                {
                    var organizerFace=(Grid)organizer.Content;
                    var organizerIcon=organizerFace.Children.OfType<System.Windows.Shapes.Path>().Single();
                    var dragOutline=organizerFace.Children.OfType<Border>().Single(b=>b.BorderThickness.Left>0);
                    if(organizerIcon.Stroke is null||organizerIcon.ActualWidth<16||dragOutline.IsVisible)
                        throw new Exception("organizer icon is missing or the drop outline is visible while idle");
                    RaiseDrag(organizer,incoming.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    if(dragOutline.Visibility!=Visibility.Visible)throw new Exception("organizer did not indicate a waiting file drop");
                    await Task.Delay(100);
                    RaiseDrag(organizer,incoming.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                    await Task.Delay(580);
                    if(popup.IsOpen||dragOutline.IsVisible)throw new Exception("short pass over organizer left a drag cue or opened the flyout");
                    RaiseDrag(organizer,incoming.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    await Task.Delay(650);
                    if(!popup.IsOpen||!projectZone.IsVisible||((FrameworkElement)popup.Child).ActualWidth>430
                        ||Rows().Any(row=>row.IsVisible)||!state.Files.QuickDropCollapsed)
                        throw new Exception("folded organizer did not show just the three temporary drop zones");
                    RaiseDrag(organizer,incoming.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                    var leave=(DispatcherTimer)typeof(DockWindow).GetField("fileLeaveDelay",widgetFlags)!.GetValue(window)!;
                    if(!projectZone.IsVisible||!leave.IsEnabled||leave.Interval<TimeSpan.FromMilliseconds(200))
                        throw new Exception("the handoff did not provide time to cross from the Dock");
                    RaiseDrag(projectZone,incoming.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    if(leave.IsEnabled)throw new Exception("reaching the flyout did not cancel the handoff timer");
                    await Task.Delay(400);
                    if(!projectZone.IsVisible)throw new Exception("hovering on a target lost the flyout");
                    RaiseDrag(projectZone,incoming.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                    await Task.Delay(450);
                    if(popup.IsOpen||!File.Exists(springSource)||popup.PlacementTarget!=originalPopupTarget)
                        throw new Exception("cancel failed to restore the panel or kept the spring-open flyout");
                    RaiseDrag(organizer,incoming.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    await Task.Delay(650);
                    var over=RaiseDrag(projectZone,incoming.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                    if(over.Effects!=DragDropEffects.Move)throw new Exception($"compact target preview: popup={popup.IsOpen}, shown={projectZone.IsVisible}, effect={over.Effects}, hint={Descendants(popup.Child).OfType<TextBlock>().Single(text=>text.Name=="QuickDropFeedback").Text}");
                    RaiseDrag(projectZone,incoming.Data,DragDropKeyStates.None,DragDrop.PreviewDropEvent);
                }
                if(popup.IsOpen||!state.Files.QuickDropCollapsed||File.Exists(springSource)
                    ||!File.Exists(Path.Combine(quickProject,"临时投放.txt"))||state.Files.SelectedSource!=originalFileSource)
                    throw new Exception("native drop did not complete and restore saved fold and source");
                // The prior native Shell drop can post a delayed foreground notification.
                await Task.Delay(160);
                popup.StaysOpen=true;
                organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(250);
                if(!popup.IsOpen||projectZone.IsVisible)throw new Exception("manual reopen must respect the saved fold");
                RaiseDrag(organizer,new DataObject(DataFormats.FileDrop,new[]{Path.Combine(quickProject,"临时投放.txt")}),
                    DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);
                await Task.Delay(650);
                if(!projectZone.IsVisible)throw new Exception("an open folded panel did not expand temporarily");
                RaiseDrag(organizer,new DataObject(DataFormats.FileDrop,new[]{Path.Combine(quickProject,"临时投放.txt")}),
                    DragDropKeyStates.None,DragDrop.DragLeaveEvent);
                await Task.Delay(450);
                if(projectZone.IsVisible||!state.Files.QuickDropCollapsed)throw new Exception("temporary reveal did not refold the open file panel");
                state.Files.QuickDropCollapsed=false;
                if(!popup.IsOpen)organizer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Find("收纳").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(350);
                Console.WriteLine("PASS folded drag-open, crossing grace, cancel recovery and native drop close");
                var beforePin=state.Items.Count;
                using(var shellData=ShellDragData.ForFiles([linkSource]))
                {
                    RaiseDrag(buttons.First(b=>AutomationProperties.GetName(b)=="添加快捷方式"),shellData.Data,DragDropKeyStates.None,DragDrop.PreviewDropEvent);
                }
                if(state.Items.Count!=beforePin+1||!state.Items.Any(i=>i.Path==linkSource)||!File.Exists(linkSource))throw new Exception("Dock drop must add an entry without relocating source");
                buttons=Descendants(window).OfType<Button>().ToList();
                popup.IsOpen=false;await Task.Delay(100);
                using(var shellData=ShellDragData.ForFiles([linkSource]))
                {
                    // Keep the synthetic source over the button, as an active OLE drag does.
                    for(int hover=0;hover<20;hover++){RaiseDrag(filesButton,shellData.Data,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent);await Task.Delay(50);}
                    if(!popup.IsOpen||Rows().Length==0){var fields=string.Join("; ",new[]{"fileDragActive","fileInitialDragView","fileHoverDirectory"}.Select(n=>n+"="+typeof(DockWindow).GetField(n,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(window)));throw new Exception($"hover opening must populate folders: open={popup.IsOpen}, rows={Rows().Length}, {fields}");}
                    RaiseDrag(filesButton,shellData.Data,DragDropKeyStates.None,DragDrop.DragLeaveEvent);await Task.Delay(200);
                }
                if(!popup.IsOpen){filesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(500);}
                if(!Descendants(popup.Child).OfType<StackPanel>().Any(p=>p.Name=="FileSidebar"))throw new Exception("file source sidebar missing");
                foreach(var title in new[]{"名称","修改时间","类型","大小"})if(!Descendants(popup.Child).OfType<TextBlock>().Any(t=>t.Text==title))throw new Exception("missing detail column "+title);
                var scroller=(ScrollViewer)typeof(DockWindow).GetField("fileScroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                scroller.ScrollToTop();await Task.Delay(100);
                var scrollBefore=(double)typeof(DockWindow).GetField("targetScroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                var wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent};scroller.RaiseEvent(wheel);
                if(!wheel.Handled)scroller.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.MouseWheelEvent});
                await Task.Delay(150);
                var scrollAfter=(double)typeof(DockWindow).GetField("targetScroll",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
                if(scroller.VerticalOffset<=0||scrollBefore!=scrollAfter)throw new Exception("file wheel must scroll its list without scrolling Dock applications");scroller.ScrollToTop();
                var oldCreated=Path.Combine(state.CollectionPath,"以前创建今天修改.txt");File.WriteAllText(oldCreated,"created earlier");File.SetCreationTimeUtc(oldCreated,DateTime.Today.ToUniversalTime().AddDays(-2));
                Find("今天新增").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                if(Rows().Any(b=>((BrowserEntry)b.Tag).Path==oldCreated))throw new Exception("today filter includes an old-created file");
                Find("今天新增").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                if(!Rows().Any(b=>((BrowserEntry)b.Tag).Path==oldCreated))throw new Exception("turning today filter off did not restore files");
                var groupButton=buttons.First(b=>AutomationProperties.GetName(b)=="应用分组");
                foreach(var group in new[]{("休闲","leisure"),("全部","all")}){
                    groupButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var groupMenu=groupButton.ContextMenu!;groupMenu.IsOpen=false;
                    groupMenu.Items.OfType<MenuItem>().Single(m=>Equals(m.Header,group.Item1)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));await Task.Delay(150);
                    if(state.SelectedGroup!=group.Item2)throw new Exception("group selector failed");
                }
                buttons=Descendants(window).OfType<Button>().ToList();
                buttons.First(b=>AutomationProperties.GetName(b)=="搜索应用或文件").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));search.Text="保留原位置";await Task.Delay(700);
                if(!Rows().Any(b=>((BrowserEntry)b.Tag).Path==linkSource&&((BrowserEntry)b.Tag).Kind=="应用"))throw new Exception("independent search did not find existing Dock app entry");
                buttons.First(b=>AutomationProperties.GetName(b)=="最近下载").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                if(state.Files.SelectedSource!="downloads")throw new Exception("downloads utility did not open download source");
                Find("收纳").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(700);
                var panelOrigin=((FrameworkElement)popup.Child).PointToScreen(new Point());var panelDpi=VisualTreeHelper.GetDpi((FrameworkElement)popup.Child);var safe=NativeServices.WorkingArea(window);
                if(panelOrigin.X/panelDpi.DpiScaleX<safe.Left-2||panelOrigin.X/panelDpi.DpiScaleX+((FrameworkElement)popup.Child).ActualWidth>safe.Right+2||panelOrigin.Y/panelDpi.DpiScaleY<safe.Top-2)throw new Exception("file panel intrudes into taskbar safe area");
                Console.WriteLine("PASS three islands, groups, application search, download utility, creation filter, file wheel and safe popup position");
                Console.WriteLine("PASS actual file-row native move, Alt link, outgoing file data and Dock reference drop routes");
                await Task.Delay(500);
                Console.WriteLine("PASS file panel recent sources, persistent search, recursive filter, folder navigation, file actions and automatic refresh");
                var fileSnapshot=args.FirstOrDefault(a=>a.StartsWith("--snapshot="));
                if(fileSnapshot is not null)CaptureElement((FrameworkElement)popup.Child,Path.ChangeExtension(fileSnapshot[11..],null)+"-files.png");
                if(popup.Child is FrameworkElement surface && (surface.ActualWidth>NativeServices.WorkingArea(window).Width||surface.ActualHeight>NativeServices.WorkingArea(window).Height))throw new Exception("file flyout exceeds monitor");
                popup.Child.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(window),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                if(popup.IsOpen)throw new Exception("Escape did not close file flyout");
                filesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var fileCover=new Window{Title="Collection close test",Width=200,Height=100,ShowInTaskbar=false};
                try {fileCover.Show();fileCover.Activate();await Task.Delay(150);if(popup.IsOpen)throw new Exception("file popup stayed above another app");}finally{fileCover.Close();}
                Console.WriteLine("PASS file flyout closes on Escape and opening another app");
                var weatherButton=Descendants(weather).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="天气与城市");
                weatherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var cityMenu=weatherButton.ContextMenu!;cityMenu.IsOpen=false;
                _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>{
                    var dialog=app.Windows.OfType<Window>().First(w=>w.Title=="选择天气城市");
                    try {
                        var apply=Descendants(dialog).OfType<Button>().First(b=>Equals(b.Content,"使用此城市"));
                        var content=(FrameworkElement)dialog.Content;
                        if(apply.TranslatePoint(new Point(0,apply.ActualHeight),content).Y>content.ActualHeight)throw new Exception("apply city button clipped");
                        Console.WriteLine("PASS city dialog action remains visible");
                    }catch(Exception ex){Console.WriteLine("FAIL "+ex);failure++;}
                    finally{dialog.Close();}
                });
                cityMenu.Items.OfType<MenuItem>().First(m=>Equals(m.Header,"更换城市…")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(!args.Contains("--live-weather")){
                    if(state.Weather?.Temperature!=18.7)throw new Exception("weather not loaded");
                    weatherHandler.Fail=true;
                    weatherButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var weatherMenu=weatherButton.ContextMenu!;weatherMenu.IsOpen=false;
                    weatherMenu.Items.OfType<MenuItem>().First(m=>Equals(m.Header,"刷新天气")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await Task.Delay(150);
                    if(state.Weather?.Temperature!=18.7||!Descendants(weather).OfType<TextBlock>().Any(t=>t.Text.Contains("离线")))throw new Exception("offline weather cache lost");
                    var offlineLamp=Descendants(weather).OfType<Border>().Single(b=>b.Name=="WeatherStatusLamp");
                    if(!SystemParameters.HighContrast && ((SolidColorBrush)offlineLamp.Background).Color!=GlassUi.Color("#D15D70").Color)
                        throw new Exception("offline weather status lamp is not using the error color");
                    Console.WriteLine("PASS weather failure retains reading and marks offline");
                    var retryTimer=(DispatcherTimer)typeof(DockWindow).GetField("weatherTimer",flags)!.GetValue(window)!;
                    if(retryTimer.Interval>TimeSpan.FromMinutes(1))throw new Exception("failed weather waits normal 20-minute interval before reconnecting");
                    weatherHandler.Fail=false;retryTimer.Stop();retryTimer.Interval=TimeSpan.FromMilliseconds(250);retryTimer.Start();
                    var recoveryDeadline=DateTime.UtcNow.AddSeconds(5);
                    while(retryTimer.Interval!=TimeSpan.FromMinutes(20)&&DateTime.UtcNow<recoveryDeadline)await Task.Delay(50);
                    if(Descendants(weather).OfType<TextBlock>().Any(t=>t.Text.Contains("离线"))||retryTimer.Interval!=TimeSpan.FromMinutes(20))throw new Exception("weather failed to recover and resume normal interval");
                    var readyLamp=Descendants(weather).OfType<Border>().Single(b=>b.Name=="WeatherStatusLamp");
                    if(!SystemParameters.HighContrast && ((SolidColorBrush)readyLamp.Background).Color!=GlassUi.Color("#46A876").Color)
                        throw new Exception("recovered weather status lamp is not using the ready color");
                    Console.WriteLine("PASS weather reconnects automatically after failure and resumes normal interval");
                }
                var iconButton = buttons.First(b=>AutomationProperties.GetName(b)==state.Items[0].Name);
                double lowerGap=Canvas.GetTop(toolbar)-iconButton.TranslatePoint(new Point(0,iconButton.ActualHeight),window).Y;
                double originalX = Canvas.GetLeft(iconButton);
                window.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120){RoutedEvent=UIElement.PreviewMouseWheelEvent});
                await Task.Delay(650);
                if (state.Items.Count>12 && Canvas.GetLeft(iconButton)>=originalX-20) throw new Exception("wheel did not move icons left");
                Console.WriteLine("PASS wheel moves icon strip left");
                var menuButton = buttons.First(b=>AutomationProperties.GetName(b)=="控制中心菜单");
                menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var menu = menuButton.ContextMenu ?? throw new Exception("menu missing");
                menu.IsOpen=false;
                MenuPath(menu,"更多","显示与位置","放到屏幕顶部").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                await Task.Delay(300);
                if(!state.TopEdge || window.Top>NativeServices.WorkingArea(window).Top+12) throw new Exception("top placement failed");
                double upperGap=Canvas.GetTop(toolbar)-iconButton.TranslatePoint(new Point(0,iconButton.ActualHeight),window).Y;
                if(Math.Abs(upperGap-lowerGap)>2)throw new Exception($"upper/lower spacing differs: upper {upperGap:F1}px, lower {lowerGap:F1}px");
                Console.WriteLine("PASS upper and lower desktop positions keep the same toolbar gap");
                Console.WriteLine("PASS top placement menu");
                var snapshot=args.FirstOrDefault(a=>a.StartsWith("--snapshot="));
                if(snapshot is not null)Capture(window,Path.ChangeExtension(snapshot[11..],null)+"-top.png");
                MenuPath(menu,"更多","显示与位置","放到屏幕底部").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,12000){RoutedEvent=UIElement.PreviewMouseWheelEvent});
                await Task.Delay(650);
                if(state.TopEdge) throw new Exception("bottom placement failed");
                Console.WriteLine("PASS bottom placement menu and scroll clamp");
                if(snapshot is not null) {
                    Capture(window,snapshot[11..]);Console.WriteLine("PASS snapshot written");
                    Capture(weather,Path.ChangeExtension(snapshot[11..],null)+"-weather.png");
                    Capture(dc,Path.ChangeExtension(snapshot[11..],null)+"-downloads.png");
                    Capture(fc,Path.ChangeExtension(snapshot[11..],null)+"-folders.png");
                    Capture(music,Path.ChangeExtension(snapshot[11..],null)+"-music.png");
                }
                // Reproduce a handled ButtonBase move route without sending OS input.
                // Put this test window under the current pointer so GetPosition is deterministic.
                var mouse=Mouse.GetPosition(window);
                var iconCenter=iconButton.TranslatePoint(new Point(iconButton.ActualWidth/2,iconButton.ActualHeight/2),window);
                window.Left+=mouse.X-iconCenter.X; window.Top+=mouse.Y-iconCenter.Y;
                await Task.Delay(100);
                iconButton.AddHandler(UIElement.MouseMoveEvent,new MouseEventHandler((_,e)=>e.Handled=true),true);
                iconButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.PreviewMouseMoveEvent});
                iconButton.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=UIElement.MouseMoveEvent});
                await Task.Delay(450);
                if(iconButton.Width<65) throw new Exception($"handled Button move lost hover route: width {iconButton.Width}");
                Console.WriteLine("PASS preview mouse route magnifies through handled Button move");
                if(snapshot is not null) Capture(window,Path.ChangeExtension(snapshot[11..],null)+"-hover.png");
                window.Width=420;
                await Task.Delay(200);
                if(buttons.First(b=>AutomationProperties.GetName(b)=="控制中心菜单").TranslatePoint(new Point(32,0),window).X>window.ActualWidth)throw new Exception("compact launcher menu overflows");
                var compactMenu=(ContextMenu)typeof(DockWindow).GetMethod("BuildMenu",flags)!.Invoke(window,null)!;
                AssertDailyMenu(compactMenu);
                compactMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"搜索应用或文件")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                if(!popup.IsOpen||state.Files.SelectedSource!="recent")throw new Exception("compact menu search did not open the recent file and application panel");
                popup.IsOpen=false;
                Console.WriteLine("PASS wide and compact menus put daily actions first and compact search opens the file panel");
                Console.WriteLine("PASS compact transparent toolbar layout");
                if(snapshot is not null)Capture(window,Path.ChangeExtension(snapshot[11..],null)+"-compact.png");

                var favorites=state.Items.Take(3).ToArray();
                state.FavoriteIds=favorites.Select(item=>item.Id).ToList();
                var standardOrder=state.Items.Select(item=>item.Id).ToArray();
                typeof(DockWindow).GetMethod("Rebuild",flags)!.Invoke(window,null);
                await Task.Delay(200);
                var row=(Canvas)typeof(DockWindow).GetField("strip",flags)!.GetValue(window)!;
                var marker=(Border)typeof(DockWindow).GetField("dropMarker",flags)!.GetValue(window)!;
                Button Favorite(int index)=>Descendants(row).OfType<Button>().First(b=>AutomationProperties.GetName(b)==favorites[index].Name);
                var first=Favorite(0);var middle=Favorite(1);var last=Favorite(2);
                double Center(Button item)=>item.TranslatePoint(new Point(item.Width/2,0),row).X;
                double firstCenter=Center(first),middleCenter=Center(middle),lastCenter=Center(last);
                var localPointer=typeof(DockWindow).GetField("pointer",flags)!;
                var localHover=typeof(DockWindow).GetField("hovering",flags)!;
                var pressField=typeof(DockWindow).GetField("pressed",flags)!;
                var favoriteTiles=(System.Collections.IList)typeof(DockWindow).GetField("tiles",flags)!.GetValue(window)!;
                localPointer.SetValue(window,new Point(firstCenter,120));localHover.SetValue(window,true);
                await Task.Delay(220);
                if(first.Content is not FrameworkElement face||face.RenderTransform is not TransformGroup feedback
                    ||feedback.Children.OfType<TranslateTransform>().Single().Y>=-1||Math.Abs(first.Width-56)>.1)
                    throw new Exception("favorite hover must lift the face while preserving the hit width");
                pressField.SetValue(window,favoriteTiles[0]);
                await Task.Delay(220);
                if(feedback.Children.OfType<ScaleTransform>().Single().ScaleX>=.98||Math.Abs(first.Width-56)>.1)
                    throw new Exception("favorite press must compress the face without shifting the hit box");
                pressField.SetValue(window,null);localHover.SetValue(window,false);

                var dragData=new DataObject("FloatingDock.Item",favorites[2].Id);
                var beforeMiddle=new Point(middleCenter-8,120);
                RaiseDrag(row,dragData,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent,beforeMiddle);
                await Task.Delay(220);
                if(!marker.IsVisible||Math.Abs(Canvas.GetLeft(marker)+marker.Width/2-middleCenter)>2
                    ||Math.Abs(Center(middle)-lastCenter)>4)
                    throw new Exception("overflow favorite opening did not show the future middle slot");
                RaiseDrag(row,dragData,DragDropKeyStates.None,DragDrop.DragLeaveEvent,beforeMiddle);
                await Task.Delay(300);
                if(marker.IsVisible||Math.Abs(Center(middle)-middleCenter)>4
                    ||!state.FavoriteIds.SequenceEqual(favorites.Select(item=>item.Id)))
                    throw new Exception("cancel failed to restore favorite layout and order");
                RaiseDrag(row,dragData,DragDropKeyStates.LeftMouseButton,DragDrop.PreviewDragOverEvent,beforeMiddle);
                await Task.Delay(180);
                RaiseDrag(row,dragData,DragDropKeyStates.None,DragDrop.PreviewDropEvent,beforeMiddle);
                await Task.Delay(200);
                var expectedOrder=new[]{favorites[0].Id,favorites[2].Id,favorites[1].Id};
                var savedPath=(string)typeof(DockWindow).GetField("statePath",flags)!.GetValue(window)!;
                if(marker.IsVisible||!state.FavoriteIds.SequenceEqual(expectedOrder)
                    ||!DockStore.Load(savedPath).FavoriteIds.SequenceEqual(expectedOrder)
                    ||!state.Items.Select(item=>item.Id).SequenceEqual(standardOrder)||Math.Abs(Center(last)-middleCenter)>2)
                    throw new Exception("favorite drop did not commit the previewed order and preserve the catalog");
                Console.WriteLine("PASS favorite face hover and press, cross-lane insertion preview, cancel and saved order");

                window.Width=650;
                var fold=buttons.Single(b=>AutomationProperties.GetName(b)=="收起 Dock");
                var foldFace=(Border)fold.Content;
                foreach(var top in new[]{true,false,true})
                {
                    MenuPath(menu,"更多","显示与位置",top?"放到屏幕顶部":"放到屏幕底部")
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    window.UpdateLayout();
                    double foldCenter=foldFace.TranslatePoint(new Point(foldFace.ActualWidth/2,foldFace.ActualHeight/2),toolbar).Y;
                    double foldInset=top?toolbar.ActualHeight-foldCenter:foldCenter;
                    if(fold.ActualWidth<40||foldInset<=0||foldInset>10
                        ||((ScaleTransform)foldFace.Child.RenderTransform).ScaleY!=(top?-1:1))
                        throw new Exception("fold glyph is outside the glass or facing the wrong edge");

                    var settledPosition=(window.Left,window.Top,window.Width,window.Height);
                    var workArea=NativeServices.WorkingArea(window);
                    fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(SystemParameters.ClientAreaAnimation)
                    {
                        var tuckWindow=app.Windows.OfType<Window>().Single(w=>w.Title=="Dock 边缘动效");
                        var tuckStage=(Canvas)tuckWindow.Content;
                        var tuckFrame=(Image)tuckStage.Children[0];
                        var tuckBitmap=(BitmapSource)tuckFrame.Source;
                        var tuckGlassPixel=new byte[4];
                        tuckBitmap.CopyPixels(new Int32Rect(tuckBitmap.PixelWidth/2,tuckBitmap.PixelHeight*7/10,1,1),tuckGlassPixel,4,0);
                        var tuckTab=app.Windows.OfType<Window>().Single(w=>w.Title=="展开 Dock");
                        if(tuckGlassPixel[3]==0||window.Opacity!=0||tuckTab.Opacity>.05
                            ||tuckWindow.IsActive||tuckStage.IsHitTestVisible
                            ||Math.Abs((top?tuckWindow.Top:tuckWindow.Top+tuckWindow.Height)-(top?workArea.Top:workArea.Bottom))>1)
                            throw new Exception("collapse must start from the visible Dock without showing the edge handle early");
                        var tuckMotion=(TransformGroup)tuckFrame.RenderTransform;
                        var towardEdge=(TranslateTransform)tuckMotion.Children[1];
                        await Task.Delay(90);
                        if(tuckWindow.IsVisible && (top ? towardEdge.Y>=-2 : towardEdge.Y<=2))
                            throw new Exception("collapse did not slide toward the selected screen edge");
                    }
                    await Task.Delay(300);
                    var handle=app.Windows.OfType<Window>().Single(w=>w.Title=="展开 Dock");
                    var reveal=(Button)handle.Content;
                    var tab=(Border)reveal.Content;
                    double tabEdge=tab.TranslatePoint(new Point(0,top?0:tab.ActualHeight),handle).Y;
                    if(!state.DockCollapsed||window.IsVisible||!handle.IsVisible||handle.IsActive
                        ||handle.Opacity!=1||(window.Left,window.Top,window.Width,window.Height)!=settledPosition
                        ||app.Windows.OfType<Window>().Any(w=>w.Title=="Dock 边缘动效")
                        ||Math.Abs(handle.Top-(top?workArea.Top:workArea.Bottom-handle.ActualHeight))>1
                        ||Math.Abs(tabEdge-(top?0:handle.ActualHeight))>1
                        ||((ScaleTransform)tab.Child.RenderTransform).ScaleY!=(top?1:-1)
                        ||handle.ActualWidth-tab.ActualWidth<16||!DockStore.Load(savedPath).DockCollapsed)
                        throw new Exception("collapsed dock lost its passive flush tab or failed to save");

                    if(!SystemParameters.HighContrast)
                    {
                        var glass=(SolidColorBrush)tab.Background;
                        var rim=(SolidColorBrush)tab.BorderBrush;
                        var idleGlass=glass.Color; var idleRim=rim.Color;
                        reveal.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseEnterEvent});
                        await Task.Delay(180);
                        if(glass.Color.A<=idleGlass.A||glass.Color.A-idleGlass.A>36||rim.Color.A<=idleRim.A
                            ||Math.Abs(tab.TranslatePoint(new Point(0,top?0:tab.ActualHeight),handle).Y-tabEdge)>1)
                            throw new Exception("handle hover must gently brighten without moving its hit area");
                        reveal.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseLeaveEvent});
                        await Task.Delay(180);
                        if(glass.Color!=idleGlass||rim.Color!=idleRim)
                            throw new Exception("handle hover did not fade back to idle");
                    }

                    reveal.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(SystemParameters.ClientAreaAnimation)
                    {
                        var revealWindow=app.Windows.OfType<Window>().Single(w=>w.Title=="Dock 边缘动效");
                        var stage=(Canvas)revealWindow.Content;
                        var frame=(Image)stage.Children[0];
                        var revealBitmap=(BitmapSource)frame.Source;
                        var glassPixel=new byte[4];
                        revealBitmap.CopyPixels(new Int32Rect(revealBitmap.PixelWidth/2,revealBitmap.PixelHeight*7/10,1,1),glassPixel,4,0);
                        if(glassPixel[3]==0||window.Opacity!=0||handle.IsVisible||revealWindow.IsActive||stage.IsHitTestVisible
                            ||Math.Abs((top?revealWindow.Top:revealWindow.Top+revealWindow.Height)-(top?workArea.Top:workArea.Bottom))>1)
                            throw new Exception("edge reveal had a blank frame, duplicated dock, or a surface away from the handle");
                        var motion=(TransformGroup)frame.RenderTransform;
                        var travel=(TranslateTransform)motion.Children[1];
                        await Task.Delay(45);
                        if(revealWindow.IsVisible && (top ? travel.Y>=-8 : travel.Y<=8))
                            throw new Exception("the Dock did not move inward from the selected edge");
                        double remaining=Math.Abs(travel.Y);
                        await Task.Delay(75);
                        if(revealWindow.IsVisible && Math.Abs(travel.Y)>=remaining-1)
                            throw new Exception("the edge reveal stalled before reaching the Dock position");
                    }
                    await Task.Delay(350);
                    if(!window.IsVisible||handle.IsVisible||state.DockCollapsed||!fold.IsHitTestVisible
                        ||window.Opacity!=1||(window.Left,window.Top,window.Width,window.Height)!=settledPosition
                        ||app.Windows.OfType<Window>().Any(w=>w.Title=="Dock 边缘动效")||DockStore.Load(savedPath).DockCollapsed)
                        throw new Exception("edge tab did not restore the dock or save the new state");

                    if(SystemParameters.ClientAreaAnimation)
                    {
                        fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var currentMotion=app.Windows.OfType<Window>().Single(w=>w.Title=="Dock 边缘动效");
                        await Task.Delay(80);
                        if(currentMotion.IsVisible)
                        {
                            window.RevealDock();
                            if(!currentMotion.IsVisible||state.DockCollapsed)
                                throw new Exception("reopening during collapse did not reuse the moving surface");
                            await Task.Delay(300);
                            if(!window.IsVisible||window.Opacity!=1||handle.IsVisible||currentMotion.IsVisible
                                ||DockStore.Load(savedPath).DockCollapsed)
                                throw new Exception("interrupted collapse left the dock, handle or saved state out of sync");
                        }
                        else
                        {
                            window.RevealDock();
                            await Task.Delay(300);
                        }
                    }
                }
                Console.WriteLine("PASS Dock slides in and out at both screen edges, hands off to the passive tab, and reverses an interrupted collapse");

                var toggleFromMenu=typeof(DockWindow).GetMethod("ToggleFromShortcut",flags)!;
                toggleFromMenu.Invoke(window,null);
                menuButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var activeDockMenu=menuButton.ContextMenu;
                if(activeDockMenu is not { IsOpen:true })throw new Exception("could not open the Dock's context menu");
                toggleFromMenu.Invoke(window,null);
                await Task.Delay(340);
                if(activeDockMenu.IsOpen||window.IsVisible||!state.DockCollapsed)
                    throw new Exception("shortcut collapse left an open Dock menu");
                Console.WriteLine("PASS shortcut collapse dismisses the Dock's own context menu");

                window.RevealDock();
                await Task.Delay(350);
                var repairDir=Path.Combine(state.CollectionPath,"入口修复测试");
                Directory.CreateDirectory(repairDir);
                var probe=Path.GetFullPath(Path.Combine("launch-probe","bin","Debug","net10.0","LaunchProbe.exe"));
                var invalidPath=Path.Combine(repairDir,"测试失效入口.lnk");
                var removedTarget=Path.Combine(repairDir,"已移除的测试目标.exe");
                var retryReport=Path.Combine(repairDir,"retry.json");
                var broken=new DockItem { Name="测试失效入口", Path=invalidPath, Group="creative" };
                var otherEntry=new DockItem { Name="测试已有入口", Path=probe };
                state.SelectedGroup="all";
                state.Items.Insert(0,broken);
                state.Items.Add(otherEntry);
                state.FavoriteIds.Insert(0,broken.Id);
                typeof(DockWindow).GetMethod("Rebuild",flags)!.Invoke(window,null);
                var issue=(System.Windows.Controls.Primitives.Popup)typeof(DockWindow).GetField("launchIssue",flags)!.GetValue(window)!;
                var card=(Border)issue.Child;
                var retry=Descendants(card).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="重试");
                var dismiss=Descendants(card).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="关闭启动提示");
                var repair=Descendants(card).OfType<Button>().Single(b=>AutomationProperties.GetName(b)=="重新选择入口");
                foreach(var top in new[]{true,false})
                {
                    MenuPath(menu,"更多","显示与位置",top?"放到屏幕顶部":"放到屏幕底部")
                        .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    await Task.Delay(110);
                    if(!top)
                    {
                        File.WriteAllText(removedTarget,"fixture");
                        MakeShortcut(invalidPath,removedTarget,"",repairDir);
                        File.Delete(removedTarget);
                        var check=typeof(App).Assembly.GetType("FloatingDock.ShortcutTarget")!
                            .GetMethod("Check",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!;
                        try
                        {
                            check.Invoke(null,new object[]{invalidPath});
                            throw new Exception("the orphaned on-disk link was not detected before launch");
                        }
                        catch(System.Reflection.TargetInvocationException error) when(error.InnerException is FileNotFoundException) { }
                    }
                    var entry=Descendants(window).OfType<Button>().Single(b=>AutomationProperties.GetName(b)==broken.Name);
                    entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Task.Delay(170);
                    if(!issue.IsOpen||issue.PlacementTarget!=entry||!retry.IsVisible||!repair.IsVisible
                        ||(GetWindowLong(new WindowInteropHelper(window).Handle,-20)&8)==0
                        ||!Descendants(card).OfType<TextBlock>().Any(b=>b.Text=="快捷方式已失效"))
                        throw new Exception("failure did not keep the Dock raised and show actions beside the specific icon");
                    var entryPoint=entry.PointToScreen(new Point(0,entry.ActualHeight/2));
                    var issuePoint=card.PointToScreen(new Point(0,card.ActualHeight/2));
                    if((issuePoint.Y>entryPoint.Y)!=top)
                        throw new Exception("the issue popup is on the wrong side of the anchored icon");
                    retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if(!issue.IsOpen)throw new Exception("failed retry lost the recovery actions");
                    if(top)
                    {
                        dismiss.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        await Task.Delay(180);
                        if(issue.IsOpen)throw new Exception("dismiss did not close the launch recovery popup");
                    }
                }
                Console.WriteLine("PASS missing shortcut and missing shortcut target show a pinned recovery card at both screen edges");

                MakeShortcut(invalidPath,probe,$"\"{retryReport}\"",repairDir);
                retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var retryDeadline=DateTime.UtcNow.AddSeconds(5);
                while(!File.Exists(retryReport)&&DateTime.UtcNow<retryDeadline) await Task.Delay(40);
                if(!File.Exists(retryReport)||issue.IsOpen||(GetWindowLong(new WindowInteropHelper(window).Handle,-20)&8)!=0)
                    throw new Exception("a restored shortcut could not be retried or Dock failed to yield after success");
                var replace=typeof(DockWindow).GetMethod("ReplaceLaunchEntry",flags)!;
                if((string?)replace.Invoke(window,new object[]{broken,probe})!="这个入口已在 Dock 中"
                    ||broken.Path!=invalidPath)
                    throw new Exception("reselecting an existing entry silently changed the Dock");
                var replacement=Path.Combine(repairDir,"新选入口.lnk");
                MakeShortcut(replacement,probe,$"\"{retryReport}\"",repairDir);
                var fixedOrder=state.FavoriteIds.ToArray();
                if(replace.Invoke(window,new object[]{broken,replacement}) is not null)
                    throw new Exception("valid replacement was rejected");
                var saved=DockStore.Load(savedPath);
                if(saved.Items[0].Id!=broken.Id||saved.Items[0].Name!="测试失效入口"||saved.Items[0].Group!="creative"
                    ||saved.Items[0].Path!=replacement||!saved.FavoriteIds.SequenceEqual(fixedOrder)
                    ||!saved.IgnoredDesktopShortcuts.Contains(invalidPath))
                    throw new Exception("replacement did not preserve the existing slot and metadata");
                Console.WriteLine("PASS retry launches a restored shortcut; replacement keeps ID, order and group and rejects duplicate paths");

            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); failure++; }
            window.Close();
        };
        timer.Start(); app.Run(window); return failure == 0 ? 0 : 1;
    }
    static MenuItem MenuPath(ItemsControl owner, params string[] labels)
    {
        foreach (var label in labels)
            owner=owner.Items.OfType<MenuItem>().Single(item=>Equals(item.Header,label));
        return (MenuItem)owner;
    }

    static void MakeShortcut(string path,string target,string arguments,string directory)
    {
        object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;
        object? link=null;
        try
        {
            link=((dynamic)shell).CreateShortcut(path);
            ((dynamic)link).TargetPath=target;
            ((dynamic)link).Arguments=arguments;
            ((dynamic)link).WorkingDirectory=directory;
            ((dynamic)link).Save();
        }
        finally
        {
            if(link is not null)Marshal.ReleaseComObject(link);
            Marshal.ReleaseComObject(shell);
        }
    }

    static void AssertDailyMenu(ContextMenu menu)
    {
        var firstActions=menu.Items.OfType<MenuItem>().Where(item=>item.IsEnabled).Select(item=>item.Header as string);
        if(!firstActions.SequenceEqual(new[]{"搜索应用或文件","打开文件收纳","最近下载","桌面分类与整理","应用分组","收起 Dock","更多"}))
            throw new Exception("The expanded and compact Dock menus must keep daily actions above management commands.");
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    static DragEventArgs RaiseDrag(UIElement target,IDataObject data,DragDropKeyStates keys,RoutedEvent routedEvent,Point? point=null)
    {
        var constructor=typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();
        var args=(DragEventArgs)constructor.Invoke([data,keys,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link,target,point??new Point(4,4)]);
        args.RoutedEvent=routedEvent;target.RaiseEvent(args);return args;
    }
    static void Capture(Window window,string path)
        =>CaptureElement(window,path);
    static void CaptureElement(FrameworkElement window,string path)
    {
        var dpi=VisualTreeHelper.GetDpi(window);
        var image=new RenderTargetBitmap((int)(window.ActualWidth*dpi.DpiScaleX),(int)(window.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
        using var output=File.Create(path);png.Save(output);
    }
    static string ArtworkHash(ImageSource source)
    {
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen())dc.DrawImage(source,new Rect(0,0,32,32));
        var bitmap=new RenderTargetBitmap(32,32,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var pixels=new byte[32*32*4];bitmap.CopyPixels(pixels,32*4,0);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
    }
    static void CaptureArtworkSheet(string path)
    {
        var entries=new List<(string Name,ImageSource Source)>();
        foreach(var name in new[]{"此电脑","回收站","控制面板","文件"})entries.Add((name,SystemArtwork.For(name)!));
        foreach(var item in new[]{("晴天",0,true),("夜晚",0,false),("晴间多云",2,true),("阴天",3,true),("雨",61,true),("雪",75,true),("雾",45,true),("雷雨",95,true)})entries.Add((item.Item1,WeatherArtwork.For(item.Item2,item.Item3)));
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(108,164,200)),null,new Rect(0,0,720,200));
            for(int i=0;i<entries.Count;i++){
                int x=i%6*120,y=i/6*100;dc.DrawImage(entries[i].Source,new Rect(x+40,y+12,40,40));
                var text=new FormattedText(entries[i].Name,System.Globalization.CultureInfo.GetCultureInfo("zh-CN"),FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),12,Brushes.White,1);
                dc.DrawText(text,new Point(x+60-text.Width/2,y+65));
            }
        }
        var image=new RenderTargetBitmap(720,200,96,96,PixelFormats.Pbgra32);image.Render(visual);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var output=File.Create(path);png.Save(output);
    }
    static bool IsAbove(IntPtr upper,IntPtr lower)
    { for(var current=GetWindow(lower,3);current!=IntPtr.Zero;current=GetWindow(current,3))if(current==upper)return true;return false; }
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle,uint command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
}

sealed class WeatherFixture:HttpMessageHandler
{
    public bool Fail;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        await Task.Delay(25,token).ConfigureAwait(false);
        long hour=DateTimeOffset.UtcNow.ToUnixTimeSeconds()/3600*3600;
        long day=new DateTimeOffset(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).Date,TimeSpan.FromHours(9)).ToUnixTimeSeconds();
        var json=System.Text.Json.JsonSerializer.Serialize(new{utc_offset_seconds=32400,current=new{temperature_2m=18.7,weather_code=2,is_day=1,apparent_temperature=18,relative_humidity_2m=56,wind_speed_10m=8},hourly=new{time=Enumerable.Range(0,6).Select(i=>hour+i*3600),temperature_2m=new[]{19,20,21,21,20,19},weather_code=new[]{2,0,0,2,3,3},is_day=new[]{1,1,1,1,1,1},precipitation_probability=new[]{0,0,0,5,10,10}},daily=new{time=Enumerable.Range(0,3).Select(i=>day+i*86400),temperature_2m_max=new[]{21,20,19},temperature_2m_min=new[]{14,13,12},weather_code=new[]{2,3,61}}});
        return new HttpResponseMessage(Fail?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(json)};
    }
}




sealed class MusicFixtureState{public bool Playing;public int Disposals;public List<string> Commands=[];}
sealed class MusicFixtureSession(MusicFixtureState state):IMusicSession
{
 public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>Task.FromResult(new MusicSnapshot{Available=true,Source="cloudmusic.exe",Title="音乐组件测试",Artist="测试歌手",State=state.Playing?"Playing":"Paused",CanToggle=true,CanPrevious=true,CanNext=true,CanSeek=true,Duration=240,Position=75});
 public Task<MusicSnapshot> CommandAsync(string command,double position,CancellationToken token){state.Commands.Add(command);if(command=="toggle")state.Playing=!state.Playing;return ReadAsync(token);}
 public void Dispose()=>state.Disposals++;
}
