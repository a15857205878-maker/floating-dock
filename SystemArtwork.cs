using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FloatingDock;

public static class SystemArtwork
{
    public static ImageSource? For(string name) => name switch
    {
        "此电脑" => Stock(94),
        "回收站" => Stock(31),
        "文件" => Stock(3),
        "控制面板" => NativeServices.IconFor(Path.Combine(Environment.SystemDirectory, "control.exe")),
        _ => null
    };

    static ImageSource? Stock(int id)
    {
        var info = new StockInfo { Size = (uint)Marshal.SizeOf<StockInfo>(), Path = "" };
        try
        {
            if (SHGetStockIconInfo(id, 0x104, ref info) != 0 || info.Icon == IntPtr.Zero) return null;
            var image = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze(); return image;
        }
        finally { if (info.Icon != IntPtr.Zero) DestroyIcon(info.Icon); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct StockInfo
    {
        public uint Size;
        public IntPtr Icon;
        public int SystemIndex, IconIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHGetStockIconInfo(int id, uint flags, ref StockInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool DestroyIcon(IntPtr icon);
}
