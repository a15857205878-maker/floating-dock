using System.Runtime.InteropServices;

namespace FloatingDock;

public static class DesktopLauncher
{
    public static void ShowDesktop()
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("13709620-C279-11CE-A49E-444553540000"), true)!);
            ((dynamic)shell!).MinimizeAll();
        }
        finally { if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell); }
    }
    public static bool IsJobBound => IsProcessInJob(new IntPtr(-1),IntPtr.Zero,out bool inJob)&&inJob;
    public static void Open(string path,string arguments="",string directory="")
    {
        object? windows=null,desktop=null,document=null,application=null;
        try
        {
            windows=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"),true)!)!;
            object location=0,root=0;int hwnd;
            desktop=((dynamic)windows).FindWindowSW(ref location,ref root,8,out hwnd,1);
            if(desktop is null)throw new InvalidOperationException("Windows 桌面尚未就绪，请稍后重试。");
            document=((dynamic)desktop).Document;
            application=((dynamic)document).Application;
            ((dynamic)application).ShellExecute(path,arguments,directory,"open",1);
        }
        finally
        {
            var released=new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach(var value in new[]{application,document,desktop,windows})
                if(value is not null&&Marshal.IsComObject(value)&&released.Add(value))Marshal.ReleaseComObject(value);
        }
    }
    [DllImport("kernel32.dll",SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool IsProcessInJob(IntPtr process,IntPtr job,[MarshalAs(UnmanagedType.Bool)]out bool inJob);
}
