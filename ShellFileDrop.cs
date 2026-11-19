using System.Windows;
using System.Runtime.InteropServices;
using ComDataObject=System.Runtime.InteropServices.ComTypes.IDataObject;

namespace FloatingDock;
public sealed class ShellFileDrop : IDisposable
{
    readonly ComDataObject data;
    readonly uint allowed;
    INativeDropTarget? target;
    bool entered;
    public DragDropEffects Effect {get;private set;}
    public ShellFileDrop(string directory,IDataObject data,DragDropEffects allowed,uint keys)
    {
        this.data=(ComDataObject)(data is DataObject value?value:new DataObject(data));this.allowed=(uint)allowed&7;
        IShellItem? item=null;
        try
        {
            var iid=typeof(IShellItem).GUID;Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(directory,IntPtr.Zero,ref iid,out item));
            var handler=new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");var drop=typeof(INativeDropTarget).GUID;
            item.BindToHandler(IntPtr.Zero,ref handler,ref drop,out var bound);target=(INativeDropTarget)bound;
            uint effect=this.allowed;Marshal.ThrowExceptionForHR(target.DragEnter(this.data,keys,CursorPoint(),ref effect));entered=true;Effect=(DragDropEffects)(effect&7);
        }
        catch{Dispose();throw;}
        finally{if(item is not null)Marshal.ReleaseComObject(item);}
    }
    public DragDropEffects Over(uint keys)
    {
        if(target is null)throw new ObjectDisposedException(nameof(ShellFileDrop));
        uint effect=allowed;Marshal.ThrowExceptionForHR(target.DragOver(keys,CursorPoint(),ref effect));return Effect=(DragDropEffects)(effect&7);
    }
    public DragDropEffects Drop(uint keys)
    {
        if(target is null)throw new ObjectDisposedException(nameof(ShellFileDrop));
        uint effect=allowed;
        try{Marshal.ThrowExceptionForHR(target.Drop(data,keys,CursorPoint(),ref effect));return Effect=(DragDropEffects)(effect&7);}
        finally{entered=false;}
    }
    public void Dispose()
    {
        if(target is null)return;
        try{if(entered)target.DragLeave();}finally{Marshal.ReleaseComObject(target);target=null;entered=false;}
    }
    [StructLayout(LayoutKind.Sequential)]struct PointL{public int X,Y;}
    static PointL CursorPoint(){GetCursorPos(out var point);return point;}
    [DllImport("user32.dll")] [return:MarshalAs(UnmanagedType.Bool)]static extern bool GetCursorPos(out PointL point);
    [ComImport,Guid("00000122-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface INativeDropTarget
    {
        [PreserveSig]int DragEnter([In,MarshalAs(UnmanagedType.Interface)]ComDataObject data,uint keys,PointL point,ref uint effect);
        [PreserveSig]int DragOver(uint keys,PointL point,ref uint effect);
        [PreserveSig]int DragLeave();
        [PreserveSig]int Drop([In,MarshalAs(UnmanagedType.Interface)]ComDataObject data,uint keys,PointL point,ref uint effect);
    }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out object result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint type,out IntPtr text);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(IShellItem other,uint hint,out int order);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)]static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,out IShellItem item);
}
