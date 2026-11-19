using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace FloatingDock;

// Wallpaper-backed frosting for the existing per-pixel transparent desktop window.
// It never captures the screen and never changes the window's native composition or Z order.
public sealed class AcrylicMaterial : Canvas,IDisposable
{
    const double Bleed=44;
    readonly Window owner;
    readonly bool glass;
    readonly double cornerRadius;
    readonly Rectangle wallpaper=new(){Name="AcrylicWallpaper",IsHitTestVisible=false,Effect=new BlurEffect{Radius=32,KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Performance}};
    readonly Rectangle tint=new(){IsHitTestVisible=false,Fill=new LinearGradientBrush(Color.FromArgb(155,57,73,94),Color.FromArgb(184,20,33,53),90)};
    readonly Rectangle grain=new(){Name="AcrylicGrain",IsHitTestVisible=false,Opacity=.055,Fill=Noise()};
    readonly Border highlight=new(){Height=1,Background=new SolidColorBrush(Color.FromArgb(95,229,241,255)),IsHitTestVisible=false};
    readonly Rectangle reflection=new(){Name="GlassReflection",IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    readonly Border lowerRim=new(){Height=1,Background=new SolidColorBrush(Color.FromArgb(65,235,247,255)),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
    readonly Dictionary<string,WallpaperImage?> images=new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> loading=new(StringComparer.OrdinalIgnoreCase);
    IReadOnlyList<WallpaperDescriptor> descriptors=[];
    int generation;
    bool disposed;
    sealed record WallpaperImage(BitmapSource Bitmap,int OriginalWidth,int OriginalHeight);

    public AcrylicMaterial(Window owner, bool light = false, bool panel = false)
    {
        this.owner=owner;glass=light;cornerRadius=light?(panel?22:26):18;Name="ToolbarAcrylic";IsHitTestVisible=false;
        if(light)
        {
            // Sample the wallpaper at full strength, then tint once, like backdrop-filter glass.
            // Attenuating the sample as well as tinting it made the previous design gray/opaque.
            wallpaper.Opacity=1;
            wallpaper.Effect=new BlurEffect{Radius=panel?22:18,KernelType=KernelType.Gaussian,RenderingBias=RenderingBias.Performance};
            tint.Fill=new SolidColorBrush(Color.FromArgb(panel?(byte)64:(byte)52,19,43,61));
            grain.Opacity=.003;
            highlight.Background=new SolidColorBrush(Color.FromArgb(panel?(byte)18:(byte)8,236,242,250));
            reflection.Fill=new LinearGradientBrush(new GradientStopCollection{new(Color.FromArgb(panel?(byte)9:(byte)12,255,255,255),0),new(Colors.Transparent,.40),new(Color.FromArgb(5,255,255,255),1)},90);
            reflection.Visibility=Visibility.Visible;lowerRim.Visibility=Visibility.Collapsed;
        }
        Children.Add(wallpaper);Children.Add(tint);Children.Add(reflection);Children.Add(grain);Children.Add(highlight);Children.Add(lowerRim);
        Loaded+=OnLoaded;SizeChanged+=OnSizeChanged;
        owner.LocationChanged+=OnMove;owner.DpiChanged+=OnDpiChanged;owner.Closed+=OnClosed;
        SystemEvents.UserPreferenceChanged+=PreferencesChanged;
    }

    void OnLoaded(object sender,RoutedEventArgs e)=>ReloadWallpaper();
    void OnMove(object? sender,EventArgs e)=>UpdateSample();
    void OnDpiChanged(object sender,DpiChangedEventArgs e)=>UpdateSample();
    void OnClosed(object? sender,EventArgs e)=>Dispose();
    void OnSizeChanged(object sender,SizeChangedEventArgs e)
    {
        Clip=new RectangleGeometry(new Rect(0,0,ActualWidth,ActualHeight),cornerRadius,cornerRadius);
        wallpaper.Width=ActualWidth+Bleed*2;wallpaper.Height=ActualHeight+Bleed*2;
        SetLeft(wallpaper,-Bleed);SetTop(wallpaper,-Bleed);
        tint.Width=grain.Width=reflection.Width=ActualWidth;tint.Height=grain.Height=reflection.Height=ActualHeight;
        highlight.Width=Math.Max(0,ActualWidth-24);SetLeft(highlight,12);SetTop(highlight,0);
        lowerRim.Width=Math.Max(0,ActualWidth-32);SetLeft(lowerRim,16);SetTop(lowerRim,Math.Max(0,ActualHeight-1));
        UpdateSample();
    }
    void PreferencesChanged(object sender,UserPreferenceChangedEventArgs e)
    {
        if(disposed||Dispatcher.HasShutdownStarted)return;
        _=Dispatcher.InvokeAsync(()=>{if(!disposed)ReloadWallpaper();});
    }
    void ReloadWallpaper()
    {
        if(disposed||!IsLoaded)return;
        generation++;images.Clear();loading.Clear();
        Visibility=SystemParameters.HighContrast?Visibility.Collapsed:Visibility.Visible;
        descriptors=DesktopWallpaper.Read();UpdateSample();
    }

    void UpdateSample()
    {
        if(disposed||!IsLoaded||ActualWidth<=0||ActualHeight<=0)return;
        var center=PointToScreen(new Point(ActualWidth/2,ActualHeight/2));
        var selected=descriptors.FirstOrDefault(d=>center.X>=d.Monitor.X&&center.X<d.Monitor.X+d.Monitor.Width&&center.Y>=d.Monitor.Y&&center.Y<d.Monitor.Y+d.Monitor.Height)??descriptors.FirstOrDefault();
        if(selected is null){wallpaper.Fill=Brushes.Transparent;return;}
        Background=glass?Brushes.Transparent:new SolidColorBrush(selected.Background);
        if(string.IsNullOrWhiteSpace(selected.Path)){wallpaper.Fill=Brushes.Transparent;return;}
        if(!images.TryGetValue(selected.Path,out var source))
        {
            wallpaper.Fill=Brushes.Transparent;
            if(loading.Add(selected.Path))LoadImage(selected.Path,generation);
            return;
        }
        if(source is null){Background=Brushes.Transparent;wallpaper.Fill=Brushes.Transparent;return;}
        var drawing=AcrylicGeometry.WallpaperBounds(source.OriginalWidth,source.OriginalHeight,selected.Monitor,selected.Placement,selected.VirtualScreen);
        var origin=PointToScreen(new Point(-Bleed,-Bleed));var dpi=VisualTreeHelper.GetDpi(this);
        var brush=new ImageBrush(source.Bitmap){ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect((drawing.X-origin.X)/dpi.DpiScaleX,(drawing.Y-origin.Y)/dpi.DpiScaleY,drawing.Width/dpi.DpiScaleX,drawing.Height/dpi.DpiScaleY),Stretch=Stretch.Fill,TileMode=selected.Placement==WallpaperPlacement.Tile?TileMode.Tile:TileMode.None,AlignmentX=AlignmentX.Left,AlignmentY=AlignmentY.Top};
        brush.Freeze();wallpaper.Fill=brush;
    }

    async void LoadImage(string path,int version)
    {
        WallpaperImage? image=null;
        try
        {
            image=await Task.Run(()=>
            {
                if(!File.Exists(path))return null;
                var uri=new Uri(path,UriKind.Absolute);
                var metadata=BitmapFrame.Create(uri,BitmapCreateOptions.DelayCreation,BitmapCacheOption.None);
                int width=metadata.PixelWidth,height=metadata.PixelHeight;
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.UriSource=uri;bitmap.CacheOption=BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth=Math.Min(2048,width);bitmap.EndInit();bitmap.Freeze();
                return new WallpaperImage(bitmap,width,height);
            });
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException or System.Runtime.InteropServices.COMException){System.Diagnostics.Trace.WriteLine("Wallpaper image unavailable: "+ex.GetType().Name);}
        if(Dispatcher.HasShutdownStarted)return;
        try { await Dispatcher.InvokeAsync(()=>{if(!disposed&&version==generation){images[path]=image;loading.Remove(path);UpdateSample();}}); }
        catch(TaskCanceledException){ }
    }

    static DrawingBrush Noise()
    {
        var random=new Random(731);var drawing=new DrawingGroup();
        for(int i=0;i<1600;i++)
        {
            var color=i%2==0?Colors.White:Colors.Black;
            double diameter=.35+random.NextDouble()*.25;
            drawing.Children.Add(new GeometryDrawing(new SolidColorBrush(color),null,new EllipseGeometry(new Point(random.NextDouble()*128,random.NextDouble()*128),diameter/2,diameter/2)));
        }
        drawing.Freeze();var brush=new DrawingBrush(drawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,128,128),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,128,128),Stretch=Stretch.Fill};brush.Freeze();return brush;
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;generation++;
        Loaded-=OnLoaded;SizeChanged-=OnSizeChanged;
        owner.LocationChanged-=OnMove;owner.DpiChanged-=OnDpiChanged;owner.Closed-=OnClosed;
        SystemEvents.UserPreferenceChanged-=PreferencesChanged;images.Clear();loading.Clear();
    }
}
