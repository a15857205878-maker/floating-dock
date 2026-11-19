using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.ComponentModel;
using FloatingDock;

static class Program
{
 [STAThread]static int Main(string[] args)
 {
  if(args.Length==2&&args[0]=="--host")return RunHost(args[1]);
  var root=Directory.GetCurrentDirectory();var probe=Path.Combine(root,"launch-probe","bin","Debug","net10.0","LaunchProbe.exe");
  var dir=Path.Combine(root,"work","launch-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var direct=Path.Combine(dir,"direct.json");var detached=Path.Combine(dir,"desktop 中文.json");
  var start=new ProcessStartInfo(probe){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(direct);start.ArgumentList.Add("中文 带空格");
  using(var p=Process.Start(start)!){if(!p.WaitForExit(8000))throw new Exception("Direct probe timed out");}
  DesktopLauncher.Open(probe,$"\"{detached}\" \"中文 带空格\"",root);
  var deadline=DateTime.UtcNow.AddSeconds(10);while(!File.Exists(detached)&&DateTime.UtcNow<deadline)Thread.Sleep(100);
  if(!File.Exists(detached)){Console.WriteLine("FAIL desktop launch produced no result");return 1;}
  using var original=JsonDocument.Parse(File.ReadAllText(direct));using var brokered=JsonDocument.Parse(File.ReadAllText(detached));
  GetWindowThreadProcessId(GetShellWindow(),out uint explorerPid);
  Console.WriteLine($"Direct parent={original.RootElement.GetProperty("ParentProcessId").GetInt32()}, inJob={original.RootElement.GetProperty("InJob").GetBoolean()}");
  Console.WriteLine($"Desktop parent={brokered.RootElement.GetProperty("ParentProcessId").GetInt32()}, inJob={brokered.RootElement.GetProperty("InJob").GetBoolean()}, Explorer={explorerPid}");
  if(brokered.RootElement.GetProperty("ParentProcessId").GetInt32()!=explorerPid){Console.WriteLine("FAIL target still inherits caller instead of Explorer");return 1;}
  if(brokered.RootElement.GetProperty("Arguments")[0].GetString()!="中文 带空格"){Console.WriteLine("FAIL arguments changed");return 1;}
  Console.WriteLine("PASS Explorer launches target independently and preserves Chinese spaced arguments");
  var linkPath=Path.Combine(dir,"中文 测试入口.lnk");var linkOutput=Path.Combine(dir,"shortcut-result.json");
  object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;
  object? link=null;
  try {
   link=((dynamic)shell).CreateShortcut(linkPath);((dynamic)link).TargetPath=probe;((dynamic)link).Arguments=$"\"{linkOutput}\" \"快捷方式 参数\"";((dynamic)link).WorkingDirectory=dir;((dynamic)link).Save();
  }finally{if(link is not null)Marshal.ReleaseComObject(link);Marshal.ReleaseComObject(shell);}
  NativeServices.Open(linkPath);WaitFor(linkOutput);
  using(var result=JsonDocument.Parse(File.ReadAllText(linkOutput))) {
   if(result.RootElement.GetProperty("ParentProcessId").GetInt32()!=explorerPid||result.RootElement.GetProperty("Arguments")[0].GetString()!="快捷方式 参数"||!string.Equals(result.RootElement.GetProperty("WorkingDirectory").GetString(),dir,StringComparison.OrdinalIgnoreCase))throw new Exception("Shortcut target arguments or working directory changed");
  }
  Console.WriteLine("PASS shortcut retains original arguments and working directory through Explorer");
  return VerifyLifetime(root,probe,dir);
 }

 static int RunHost(string dir)
 {
  var go=Path.Combine(dir,"host-go");WaitFor(go);
  var probe=Path.Combine(Directory.GetCurrentDirectory(),"launch-probe","bin","Debug","net10.0","LaunchProbe.exe");
  var direct=Path.Combine(dir,"job-direct.json");var detached=Path.Combine(dir,"job-desktop.json");
  var start=new ProcessStartInfo(probe){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add(direct);start.ArgumentList.Add("--wait");Process.Start(start);
  DesktopLauncher.Open(probe,$"\"{detached}\" \"中文 带空格\" --wait",Directory.GetCurrentDirectory());
  WaitFor(direct);WaitFor(detached);File.WriteAllText(Path.Combine(dir,"host-ready"),"ready");Thread.Sleep(30000);return 0;
 }
 static void WaitFor(string path)
 {
  var deadline=DateTime.UtcNow.AddSeconds(12);while(!File.Exists(path)&&DateTime.UtcNow<deadline)Thread.Sleep(100);
  if(!File.Exists(path))throw new TimeoutException("Missing test signal: "+Path.GetFileName(path));
 }
 static int VerifyLifetime(string root,string probe,string dir)
 {
  var job=CreateJobObject(IntPtr.Zero,null);if(job==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
  Process? host=null,independent=null;
  try
  {
   var limits=new ExtendedLimits{Basic=new BasicLimits{Flags=0x2000}};
   if(!SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf<ExtendedLimits>()))throw new Win32Exception(Marshal.GetLastWin32Error());
   var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};start.ArgumentList.Add("--host");start.ArgumentList.Add(dir);
   host=Process.Start(start)!;
   if(!AssignProcessToJobObject(job,host.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());
   File.WriteAllText(Path.Combine(dir,"host-go"),"go");WaitFor(Path.Combine(dir,"host-ready"));
   using var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"job-direct.json")));
   using var b=JsonDocument.Parse(File.ReadAllText(Path.Combine(dir,"job-desktop.json")));
   using var direct=Process.GetProcessById(d.RootElement.GetProperty("ProcessId").GetInt32());
   independent=Process.GetProcessById(b.RootElement.GetProperty("ProcessId").GetInt32());
   CloseHandle(job);job=IntPtr.Zero;
   if(!host.WaitForExit(5000)||!direct.WaitForExit(5000))throw new Exception("Caller job did not end the direct child");
   if(independent.HasExited)throw new Exception("Desktop-launched target died with caller job");
   Console.WriteLine("PASS closing caller job terminates its direct child while Explorer-launched target survives");
   return 0;
  }
  finally
  {
   if(job!=IntPtr.Zero)CloseHandle(job);
   if(host is not null){if(!host.HasExited)host.Kill();host.Dispose();}
   if(independent is not null){if(!independent.HasExited&&string.Equals(independent.MainModule?.FileName,probe,StringComparison.OrdinalIgnoreCase))independent.Kill();independent.Dispose();}
  }
 }
 [StructLayout(LayoutKind.Sequential)]struct BasicLimits{public long ProcessTime,JobTime;public uint Flags;public UIntPtr MinWorkingSet,MaxWorkingSet;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint Priority,Scheduling;}
 [StructLayout(LayoutKind.Sequential)]struct Counters{public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
 [StructLayout(LayoutKind.Sequential)]struct ExtendedLimits{public BasicLimits Basic;public Counters Io;public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;}
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateJobObject(IntPtr security,string? name);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimits limits,uint length);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
}
