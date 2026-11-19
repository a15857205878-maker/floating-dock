using System.Net.Http;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using FloatingDock;
class Program {
 [STAThread] static int Main(){
  var root=Path.Combine(Path.GetTempPath(),"dock-refresh-"+Guid.NewGuid());Directory.CreateDirectory(root);
  var red=Path.Combine(root,"red.ico");var blue=Path.Combine(root,"blue.ico");
  WriteIcon(red,35,35,240);WriteIcon(blue,240,35,35);
  var shortcut=Path.Combine(root,"fixture.lnk");Save(shortcut,red);
  var app=new Application();var state=new DockState{AutoImportDesktop=false,CollectionPath=Path.Combine(root,"collection")};state.Items.Add(new DockItem{Name="fixture",Path=shortcut});
  var window=new DockWindow(state,Path.Combine(root,"settings.json"),new WeatherService(new HttpClient(new Offline())));
  int result=1;
  window.Loaded+=async(_,_)=>{try{
   var image=Desc(window).OfType<Button>().Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="fixture").Content as Image??throw new Exception("fixture icon image missing");
   await Until(()=>image.Source is BitmapSource);var original=image.Source;var before=Pixels((BitmapSource)original);
   Save(shortcut,blue);
   await Until(()=>image.Source!=original);var after=Pixels((BitmapSource)image.Source);
   if(before.SequenceEqual(after))throw new Exception("refreshed source still contains old pixels");
   Console.WriteLine("PASS changed shortcut refreshes automatically with new icon pixels");
   File.Delete(shortcut);await Task.Delay(1800);Save(shortcut,red);
   await Until(()=>image.Source is BitmapSource b&&Pixels(b).SequenceEqual(before));
   Console.WriteLine("PASS shortcut recreation refreshes and fallback recovers");result=0;
  }catch(Exception e){Console.WriteLine("FAIL "+e);}finally{window.Close();app.Shutdown();}};
  try{app.Run(window);return result;}finally{Directory.Delete(root,true);}
 }
 static async Task Until(Func<bool> test){for(int i=0;i<60;i++){if(test())return;await Task.Delay(100);}throw new Exception("icon refresh timed out");}
 static byte[] Pixels(BitmapSource b){var data=new byte[b.PixelHeight*((b.PixelWidth*b.Format.BitsPerPixel+7)/8)];b.CopyPixels(data,(b.PixelWidth*b.Format.BitsPerPixel+7)/8,0);return data;}
 static void WriteIcon(string path,byte b,byte g,byte r){
  var pixels=new byte[32*32*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=b;pixels[i+1]=g;pixels[i+2]=r;pixels[i+3]=255;}
  var image=BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,pixels,128);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var png=new MemoryStream();encoder.Save(png);var bytes=png.ToArray();
  using var output=new BinaryWriter(File.Create(path));output.Write((ushort)0);output.Write((ushort)1);output.Write((ushort)1);output.Write((byte)32);output.Write((byte)32);output.Write((ushort)0);output.Write((ushort)1);output.Write((ushort)32);output.Write(bytes.Length);output.Write(22);output.Write(bytes);
 }
 static void Save(string path,string icon){object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)!)!;object? link=null;try{link=((dynamic)shell).CreateShortcut(path);((dynamic)link).TargetPath=Environment.ProcessPath;((dynamic)link).IconLocation=icon+",0";((dynamic)link).Save();}finally{if(link!=null)Marshal.ReleaseComObject(link);Marshal.ReleaseComObject(shell);}}
 static IEnumerable<DependencyObject> Desc(DependencyObject x){for(int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++){var c=VisualTreeHelper.GetChild(x,i);yield return c;foreach(var d in Desc(c))yield return d;}}
 class Offline:HttpMessageHandler{protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)=>Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));}
}
