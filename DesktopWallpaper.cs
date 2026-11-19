using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace FloatingDock;

internal sealed record WallpaperDescriptor(string Path,MaterialRect Monitor,MaterialRect VirtualScreen,WallpaperPlacement Placement,Color Background);

internal static class DesktopWallpaper
{
    // This is a read-only Shell interface. No wallpaper or desktop settings are changed.
    public static IReadOnlyList<WallpaperDescriptor> Read()
    {
        IDesktopWallpaper? service=null;
        try
        {
            service=(IDesktopWallpaper)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"),true)!)!;
            service.GetMonitorDevicePathCount(out uint count);
            service.GetPosition(out WallpaperPlacement placement);
            service.GetBackgroundColor(out uint color);
            var monitors=new List<(string Path,Rect Bounds)>();
            Rect virtualScreen=Rect.Empty;
            for(uint i=0;i<count;i++)
            {
                service.GetMonitorDevicePathAt(i,out string monitor);
                service.GetMonitorRECT(monitor,out var rect);
                if(rect.Right<=rect.Left||rect.Bottom<=rect.Top)continue;
                service.GetWallpaper(monitor,out string path);
                var bounds=new Rect(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top);
                virtualScreen.Union(bounds);monitors.Add((path,bounds));
            }
            var background=Color.FromRgb((byte)(color&255),(byte)((color>>8)&255),(byte)((color>>16)&255));
            return monitors.Select(m=>new WallpaperDescriptor(m.Path,new(m.Bounds.X,m.Bounds.Y,m.Bounds.Width,m.Bounds.Height),new(virtualScreen.X,virtualScreen.Y,virtualScreen.Width,virtualScreen.Height),placement,background)).ToArray();
        }
        catch(COMException ex){System.Diagnostics.Trace.WriteLine($"Wallpaper API unavailable: 0x{ex.HResult:X8}");return [];}
        catch(UnauthorizedAccessException){return [];}
        finally{if(service is not null)Marshal.ReleaseComObject(service);}
    }

    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
    [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string? monitor,[MarshalAs(UnmanagedType.LPWStr)]string path);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]out string path);
        void GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out NativeRect rect);
        void SetBackgroundColor(uint color);
        void GetBackgroundColor(out uint color);
        void SetPosition(WallpaperPlacement position);
        void GetPosition(out WallpaperPlacement position);
    }
}
