using System.Runtime.InteropServices;
using System.Windows;
using ComDataObject=System.Runtime.InteropServices.ComTypes.IDataObject;

namespace FloatingDock;

public sealed class ShellDragData : IDisposable
{
    ComDataObject? native;
    public DataObject Data {get;}
    ShellDragData(ComDataObject data){native=data;Data=new DataObject(data);}
    public static ShellDragData ForFiles(IEnumerable<string> paths)
    {
        var pidls=new List<IntPtr>();IShellItemArray? array=null;
        try
        {
            foreach(var path in paths)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(path,IntPtr.Zero,out var pidl,0,out _));pidls.Add(pidl);
            }
            if(pidls.Count==0)throw new ArgumentException("没有选择文件。");
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Count,pidls.ToArray(),out array));
            var handler=new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");var iid=typeof(ComDataObject).GUID;
            array.BindToHandler(IntPtr.Zero,ref handler,ref iid,out var data);return new((ComDataObject)data);
        }
        finally
        {
            if(array is not null)Marshal.ReleaseComObject(array);foreach(var pidl in pidls)Marshal.FreeCoTaskMem(pidl);
        }
    }
    public void Dispose(){if(native is not null){Marshal.ReleaseComObject(native);native=null;}}
    [ComImport,Guid("B63EA76D-1F85-456F-A19C-48159EFA858B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemArray
    {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object data);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHParseDisplayName(string name,IntPtr context,out IntPtr pidl,uint attributesIn,out uint attributesOut);
    [DllImport("shell32.dll",PreserveSig=true)]static extern int SHCreateShellItemArrayFromIDLists(uint count,[MarshalAs(UnmanagedType.LPArray,SizeParamIndex=0)]IntPtr[] pidls,out IShellItemArray array);
}
