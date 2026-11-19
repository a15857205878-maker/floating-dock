using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FloatingDock;
public interface IMusicSession:IDisposable
{
 Task<MusicSnapshot> ReadAsync(CancellationToken token);
 Task<MusicSnapshot> CommandAsync(string command,double position,CancellationToken token);
}
public sealed class MusicSessionBridge:IMusicSession
{
 readonly SemaphoreSlim gate=new(1,1);
 readonly CancellationTokenSource lifetime=new();
 readonly object sync=new();
 Process? worker;
 bool disposed;
 public Task<MusicSnapshot> ReadAsync(CancellationToken token)=>CommandAsync("poll",0,token);
 public async Task<MusicSnapshot> CommandAsync(string command,double position,CancellationToken token)
 {
  if(command is not ("poll" or "toggle" or "previous" or "next" or "seek"))throw new ArgumentException("Unknown media command",nameof(command));
  using var stop=CancellationTokenSource.CreateLinkedTokenSource(token,lifetime.Token);stop.CancelAfter(TimeSpan.FromSeconds(9));
  await gate.WaitAsync(stop.Token).ConfigureAwait(false);
  try{
   var process=GetWorker();
   var request=JsonSerializer.Serialize(new{command,position=double.IsFinite(position)?position:0});
   await process.StandardInput.WriteLineAsync(request.AsMemory(),stop.Token).ConfigureAwait(false);await process.StandardInput.FlushAsync(stop.Token).ConfigureAwait(false);
   string? reply=await process.StandardOutput.ReadLineAsync(stop.Token).ConfigureAwait(false);
   if(reply is null)throw new IOException("系统媒体连接已结束");
   return MusicSnapshot.Parse(reply);
  }catch{StopWorker();throw;}finally{gate.Release();}
 }
 Process GetWorker()
 {
  lock(sync){
   ObjectDisposedException.ThrowIf(disposed,this);
   if(worker is {HasExited:false})return worker;
   worker?.Dispose();
   var script=Path.Combine(AppContext.BaseDirectory,"scripts","NetEaseMediaBridge.ps1");
   var encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(File.ReadAllText(script,Encoding.UTF8)));
   if(encoded.Length>28000)throw new IOException("媒体连接脚本超过启动限制");
   var start=new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=new UTF8Encoding(false),StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
   foreach(var argument in new[]{"-NoLogo","-NoProfile","-NonInteractive","-EncodedCommand",encoded})start.ArgumentList.Add(argument);
   worker=Process.Start(start)??throw new IOException("无法创建系统媒体连接");
   worker.ErrorDataReceived+=(_,_)=>{};worker.BeginErrorReadLine();return worker;
  }
 }
 void StopWorker()
 {
  lock(sync){var process=worker;worker=null;if(process is null)return;
   try{if(!process.HasExited){try{process.StandardInput.Close();}catch(IOException){}if(!process.WaitForExit(250)){process.Kill();process.WaitForExit(500);}}}catch(InvalidOperationException){}catch(IOException){}catch(System.ComponentModel.Win32Exception){}finally{process.Dispose();}
  }
 }
 public void Dispose(){lock(sync){if(disposed)return;disposed=true;lifetime.Cancel();}StopWorker();}
}
public static class NetEaseClient
{
 public static string? InstalledPath()=>new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Netease","CloudMusic","cloudmusic.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Netease","CloudMusic","cloudmusic.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Netease","CloudMusic","cloudmusic.exe")}.FirstOrDefault(File.Exists);
 public static void Open(){var path=InstalledPath()??throw new FileNotFoundException("未找到网易云音乐，请先安装客户端。");DesktopLauncher.Open(path);}
}
