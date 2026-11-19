using System.Runtime.InteropServices;
using System.Text.Json;

var current=Environment.ProcessId;
int parent=0;
var snapshot=Native.CreateToolhelp32Snapshot(2,0);
try {
 var entry=new Native.Entry{Size=(uint)Marshal.SizeOf<Native.Entry>()};
 if(Native.Process32First(snapshot,ref entry))do {if(entry.ProcessId==current){parent=(int)entry.ParentId;break;}}while(Native.Process32Next(snapshot,ref entry));
}finally{Native.CloseHandle(snapshot);}
Native.IsProcessInJob(new IntPtr(-1),IntPtr.Zero,out bool inJob);
File.WriteAllText(args[0],JsonSerializer.Serialize(new{ProcessId=current,ParentProcessId=parent,InJob=inJob,WorkingDirectory=Environment.CurrentDirectory,Arguments=args.Skip(1).ToArray()}));
if(args.Contains("--wait"))Thread.Sleep(30000);

static class Native
{
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]public struct Entry{public uint Size,Usage,ProcessId;public UIntPtr Heap;public uint Module,Threads,ParentId;public int Priority;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;}
 [DllImport("kernel32.dll")]public static extern IntPtr CreateToolhelp32Snapshot(uint flags,uint process);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern bool Process32First(IntPtr snapshot,ref Entry entry);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]public static extern bool Process32Next(IntPtr snapshot,ref Entry entry);
 [DllImport("kernel32.dll")]public static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")]public static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool result);
}
