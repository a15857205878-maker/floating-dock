using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingDock;

public static class NativeServices
{
    public static IEnumerable<string> DesktopShortcutDirectories()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return new[]
        {
            desktop,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(desktop, "桌面快捷方式")
        }.Distinct(StringComparer.OrdinalIgnoreCase);
    }
    public static IEnumerable<string> DesktopShortcuts()
    {
        foreach (var dir in DesktopShortcutDirectories())
        {
            if (!Directory.Exists(dir)) continue;
            string[] files;
            try { files = Directory.GetFiles(dir); } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
            foreach (var path in files.OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
                if (new[] { ".lnk", ".url", ".appref-ms" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) yield return path;
        }
    }

    public static void Open(string path)
    {
        ShortcutTarget.Check(path);
        LifecycleLog.Write("open "+Path.GetFileName(path));
        DesktopLauncher.Open(path);
    }

    public static void Reveal(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.Arguments = "/select,\"" + path.Replace("\"", "") + "\"";
        Process.Start(start);
    }

    // Called on a background thread: extraction may invoke Shell extensions.
    public static ImageSource? IconFor(string path)
    {
        int initialized = CoInitializeEx(IntPtr.Zero, 0);
        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            int result = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out factory);
            if (result != 0 || factory is null) return null;
            factory.GetImage(new NativeSize { Width = 128, Height = 128 }, 0x5, out bitmap);
            if (bitmap == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (COMException) { return null; }
        catch (ArgumentException) { return null; }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (factory is not null) Marshal.ReleaseComObject(factory);
            if (initialized >= 0) CoUninitialize();
        }
    }

    public static Rect WorkingArea(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle != IntPtr.Zero && GetMonitorInfo(MonitorFromWindow(handle, 2), ref info))
        {
            var transform = PresentationSource.FromVisual(window)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var monitor=new Rect(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top);
            var safe=DockLayout.SafeArea(new(monitor.Left,monitor.Top,monitor.Width,monitor.Height),new(info.Work.Left,info.Work.Top,info.Work.Right-info.Work.Left,info.Work.Bottom-info.Work.Top),NativeTaskbar.LeftWidth(monitor));
            return new MatrixTransform(transform).TransformBounds(new Rect(safe.Left,safe.Top,safe.Width,safe.Height));
        }
        return SystemParameters.WorkArea;
    }

    public static (double X, double Y) PixelPosition(Window window)
    {
        return GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds) ? (bounds.Left, bounds.Top) : (0, 0);
    }

    public static void RestorePixelPosition(Window window, double x, double y)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        var monitor = MonitorFromPoint(new NativePoint { X = (int)x, Y = (int)y }, 2);
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(handle, out var bounds)) return;
        var position = DockMath.KeepVisible(x, y, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        // Physical coordinates select the original monitor before WPF performs its per-monitor DPI conversion.
        SetWindowPos(handle, IntPtr.Zero, (int)position.X, (int)position.Y, 0, 0, 0x15);
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { void GetImage(NativeSize size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)] static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DeleteObject(IntPtr obj);
    [DllImport("ole32.dll")] static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")] static extern void CoUninitialize();
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr handle, out NativeRect bounds);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
