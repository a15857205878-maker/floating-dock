using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FloatingDock;

public sealed partial class DockWindow
{
    const double DockEdgeScale = .88;
    sealed record DockEdgeMotion(Window Surface, Window Handle, Image Frame, ScaleTransform Scale,
        TranslateTransform Slide, double EdgeOffset);

    DockEdgeMotion? dockEdgeMotion;

    DockEdgeMotion CreateDockEdgeMotion(bool expand)
    {
        // The normal window stops short of the edge. Use a clipped, input-transparent
        // surface for the motion so the saved position and the widgets stay in place.
        var handle = EnsureDockHandle();
        PositionDockHandle();
        bool fromTop = state.TopEdge;
        double edge = fromTop ? handle.Top : handle.Top + handle.Height;
        double origin = fromTop ? edge : Top;
        double end = fromTop ? Top + Height : edge;
        double leadingY = Canvas.GetTop(toolbarBackground) + (fromTop ? toolbarBackground.Height : 0);
        double anchorY = edge + (fromTop ? 1 : -1) * handleFace!.Height / 2;
        double edgeOffset = anchorY - (Top + leadingY);

        Opacity = 0;
        root.Opacity = 1;
        WindowState = WindowState.Normal;
        if (!IsVisible) Show();
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(root);
        var snapshot = new RenderTargetBitmap(
            (int)Math.Ceiling(Width * dpi.DpiScaleX), (int)Math.Ceiling(Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            var bounds = new Rect(0, 0, Width, Height);
            var source = new VisualBrush(root)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = bounds
            };
            context.DrawRectangle(source, null, bounds);
        }
        snapshot.Render(drawing);
        snapshot.Freeze();

        double anchorX = Math.Clamp(handle.Left + handle.Width / 2 - Left, 0, Width);
        double startScale = expand ? DockEdgeScale : 1;
        var scale = new ScaleTransform(startScale, startScale, anchorX, leadingY);
        var slide = new TranslateTransform(0, expand ? edgeOffset : 0);
        var motion = new TransformGroup { Children = { scale, slide } };
        var frame = new Image { Source = snapshot, Width = Width, Height = Height, RenderTransform = motion };
        Canvas.SetTop(frame, Top - origin);
        var view = new Canvas { ClipToBounds = true, IsHitTestVisible = false, Children = { frame } };
        var surface = new Window
        {
            Title = "Dock 边缘动效", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent,
            ShowInTaskbar = false, ShowActivated = false,
            Left = Left, Top = origin, Width = Width, Height = end - origin, Content = view
        };
        surface.SourceInitialized += (_, _) =>
        {
            DesktopLayer.Attach(surface);
            var hwnd = new WindowInteropHelper(surface).Handle;
            const int extendedStyle = -20;
            const int transparentAndPassive = 0x00000020 | 0x08000000;
            SetEdgeWindowStyle(hwnd, extendedStyle, GetEdgeWindowStyle(hwnd, extendedStyle) | transparentAndPassive);
        };
        return new DockEdgeMotion(surface, handle, frame, scale, slide, edgeOffset);
    }

    void AnimateDockEdge(bool expand)
    {
        bool fresh = dockEdgeMotion is null;
        var motion = dockEdgeMotion ??= CreateDockEdgeMotion(expand);
        double targetY = expand ? 0 : motion.EdgeOffset;
        double targetScale = expand ? 1 : DockEdgeScale;
        double distance = Math.Abs(targetY - motion.Slide.Y) / Math.Max(1, Math.Abs(motion.EdgeOffset));
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(250 * distance, 85, 250));
        DoubleAnimation EaseFrom(double start, double finish) => new(start, finish, duration)
        {
            EasingFunction = new QuadraticEase { EasingMode = expand ? EasingMode.EaseOut : EasingMode.EaseIn }
        };

        var version = dockTransition;
        var travel = EaseFrom(motion.Slide.Y, targetY);
        travel.Completed += (_, _) =>
        {
            if (dockEdgeMotion == motion && version == dockTransition) FinishDockEdgeMotion();
        };
        motion.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, EaseFrom(motion.Scale.ScaleX, targetScale));
        motion.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, EaseFrom(motion.Scale.ScaleY, targetScale));
        motion.Slide.BeginAnimation(TranslateTransform.YProperty, travel);

        if (expand)
        {
            if (fresh) motion.Handle.Hide();
            else
            {
                var fade = TimeSpan.FromMilliseconds(70);
                FadeDockEdge(motion.Handle, 0, TimeSpan.Zero, fade);
                FadeDockEdge(motion.Frame, 1, TimeSpan.Zero, fade);
            }
        }
        else
        {
            if (!motion.Handle.IsVisible)
            {
                motion.Handle.Opacity = 0;
                motion.Handle.Show();
            }
            var fade = TimeSpan.FromMilliseconds(68);
            var handoff = duration - fade;
            FadeDockEdge(motion.Frame, 0, handoff, fade);
            FadeDockEdge(motion.Handle, 1, handoff, fade);
        }
        if (fresh) motion.Surface.Show();
        if (shortcutPresentation.IsEnabled) DesktopLayer.SetShortcutPresentation(motion.Surface, true);
    }

    static void FadeDockEdge(UIElement visual, double target, TimeSpan delay, TimeSpan duration)
    {
        double start = visual.Opacity;
        visual.BeginAnimation(OpacityProperty, null);
        visual.Opacity = start;
        visual.BeginAnimation(OpacityProperty, new DoubleAnimation(start, target, duration) { BeginTime = delay });
    }

    void FinishDockEdgeMotion()
    {
        if (dockEdgeMotion is not { } motion) return;
        dockEdgeMotion = null;
        SettleDockVisibility();
        motion.Surface.Close();
    }

    void CloseDockEdgeMotion()
    {
        var motion = dockEdgeMotion;
        dockEdgeMotion = null;
        motion?.Surface.Close();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    static extern int GetEdgeWindowStyle(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    static extern int SetEdgeWindowStyle(IntPtr hwnd, int index, int style);
}
