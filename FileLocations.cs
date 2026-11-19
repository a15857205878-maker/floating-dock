using System.IO;
using System.Runtime.InteropServices;

namespace FloatingDock;

public static class FileLocations
{
    public static string Downloads()
    {
        var id=new Guid("374DE290-123F-4565-9164-39C4925E467B");
        IntPtr value=IntPtr.Zero;
        try
        {
            if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out value)>=0)
            {
                var path=Marshal.PtrToStringUni(value);
                if(!string.IsNullOrWhiteSpace(path))return path;
            }
        }
        finally {if(value!=IntPtr.Zero)Marshal.FreeCoTaskMem(value);}
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    }
    [DllImport("shell32.dll")]static extern int SHGetKnownFolderPath(ref Guid folder,uint flags,IntPtr token,out IntPtr path);
}
