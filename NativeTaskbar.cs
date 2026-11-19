using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace FloatingDock;

public static class NativeTaskbar
{
    public static double LeftWidth(Rect monitor)
    {
        double width=0;
        var bar=new AppBarData{Size=(uint)Marshal.SizeOf<AppBarData>(),Edge=0,Bounds=new NativeRect{Left=(int)monitor.Left,Top=(int)monitor.Top,Right=(int)monitor.Right,Bottom=(int)monitor.Bottom}};
        var auto=SHAppBarMessage(11,ref bar);
        if(auto!=UIntPtr.Zero){var handle=new IntPtr(unchecked((long)auto.ToUInt64()));width=GetWindowRect(handle,out var r)?Math.Max(16,r.Right-r.Left):64;}
        EnumWindows((handle,unused)=>{
            var name=new StringBuilder(96);GetClassName(handle,name,name.Capacity);
            if(name.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")||!GetWindowRect(handle,out var r))return true;
            double w=r.Right-r.Left,h=r.Bottom-r.Top;
            if(h>=monitor.Height*.5&&w>0&&w<monitor.Width*.3&&r.Top<=monitor.Top+monitor.Height/2&&r.Bottom>=monitor.Top+monitor.Height/2&&(Math.Abs(r.Left-monitor.Left)<=12||Math.Abs(r.Right-monitor.Left)<=12))width=Math.Max(width,w);
            return true;
        },IntPtr.Zero);
        return Math.Min(width,monitor.Width*.3);
    }
    delegate bool Visitor(IntPtr handle,IntPtr unused);
    [StructLayout(LayoutKind.Sequential)]struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)]struct AppBarData{public uint Size;public IntPtr Window;public uint Callback,Edge;public NativeRect Bounds;public IntPtr Parameter;}
    [DllImport("shell32.dll")]static extern UIntPtr SHAppBarMessage(uint message,ref AppBarData data);
    [DllImport("user32.dll")]static extern bool EnumWindows(Visitor visitor,IntPtr unused);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr handle,StringBuilder name,int capacity);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr handle,out NativeRect bounds);
}
