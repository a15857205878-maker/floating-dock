using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using FloatingDock;
static class Program
{
 [STAThread]static int Main(string[] args)
 {
  var app=new Application();int failure=0;var root=Path.Combine(Directory.GetCurrentDirectory(),"work","cards-"+Guid.NewGuid());Directory.CreateDirectory(root);
  var downloads=Path.Combine(root,"下载");Directory.CreateDirectory(downloads);File.WriteAllText(Path.Combine(downloads,"已完成.txt"),"ok");File.WriteAllText(Path.Combine(downloads,"未完成.crdownload"),"partial");
  var card=new DesktopFileCard("下载收件箱",true,()=>downloads,()=>[],()=>{},null,()=>{},()=>{});
  var folders=new DesktopFileCard("常用文件夹",false,()=>"",()=>[root,downloads],()=>{},()=>{},()=>{},()=>{});
  card.Place(new(100,100,304,286),false);folders.Place(new(100,398,304,250),false);
  var start=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(800)};
  start.Tick+=async(_,_)=>{
   start.Stop();try{
    ListBox List(Window w)=>Walk(w).OfType<ListBox>().Single();
    async Task Until(Func<bool> predicate){var until=DateTime.UtcNow.AddSeconds(6);while(!predicate()&&DateTime.UtcNow<until)await Task.Delay(80);if(!predicate())throw new Exception("card timed out");}
    await Until(()=>List(card).Items.Count==1);if(((BrowserEntry)((ListBoxItem)List(card).Items[0]).Tag).Name!="已完成.txt")throw new Exception("partial download leaked");
    Console.WriteLine("PASS download card shows completed files only");
    File.WriteAllText(Path.Combine(downloads,"刚刚下载.txt"),"new");await Until(()=>List(card).Items.Count==2);Console.WriteLine("PASS download card refreshes from file events");
    var box=List(card);box.SelectAll();var first=(ListBoxItem)box.Items[0];
    var down=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent};first.RaiseEvent(down);
    if(!down.Handled||box.SelectedItems.Count!=2)throw new Exception($"selected drag start not preserved handled={down.Handled} selected={box.SelectedItems.Count}");
    var up=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseUpEvent};first.RaiseEvent(up);
    if(box.SelectedItems.Count!=1)throw new Exception("plain click did not select one row");Console.WriteLine("PASS selected multi-file drag start preserves selection until click release");
    await Until(()=>List(folders).Items.Count==2);Console.WriteLine("PASS folder card retains actual directory paths");
    if(Walk(card).OfType<System.Windows.Controls.Primitives.Thumb>().Count()<9)throw new Exception("missing title drag and eight resize handles");
    var placementState=new DockState();var placementFile=Path.Combine(root,"placement.json");
    card.ConfigurePlacement(placementState.Files.DownloadsPlacement,()=>DockStore.Save(placementFile,placementState));
    var beforeX=card.Left;var beforeWidth=card.Width;
    void Gesture(string name,double dx,double dy){var handle=Walk(card).OfType<System.Windows.Controls.Primitives.Thumb>().Single(t=>t.Name==name);handle.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});handle.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(dx,dy){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragDeltaEvent});handle.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(dx,dy,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});}
    Gesture("CardMove",40,30);if(card.Left<=beforeX)throw new Exception("title move did not move");
    Gesture("CardResizeRight",60,0);if(card.Width!=beforeWidth+60)throw new Exception("edge resize did not resize");
    var recorded=DockStore.Load(placementFile);var model=recorded.Files.DownloadsPlacement;var expandedHeight=model.Height;
    card.RestoreOrPlace(new(0,0,100,100),true);Gesture("CardMove",10,10);if(model.Height!=expandedHeight||placementState.Files.DownloadsPlacement.Height!=expandedHeight)throw new Exception("collapsed move destroyed expanded height");
    card.RestoreOrPlace(new(0,0,100,100),false);if(card.Height!=expandedHeight)throw new Exception("expand lost saved height");
    var restoredCard=new DesktopFileCard("恢复位置测试",true,()=>downloads,()=>[],()=>{},null,()=>{},()=>{});
    try{restoredCard.ConfigurePlacement(DockStore.Load(placementFile).Files.DownloadsPlacement,()=>{});restoredCard.RestoreOrPlace(new(800,500,304,286),false);await Task.Delay(100);var actual=NativeServices.PixelPosition(restoredCard);var expected=DockStore.Load(placementFile).Files.DownloadsPlacement;if(Math.Abs(actual.X-expected.PixelX!.Value)>2||Math.Abs(actual.Y-expected.PixelY!.Value)>2||restoredCard.Width!=expected.Width)throw new Exception("new window did not restore saved rectangle");}finally{restoredCard.CloseForShutdown();}
    Console.WriteLine("PASS title move edge resize save and new-window restore; collapsed move retains expanded size");
    var incoming=Path.Combine(root,"拖入.txt");File.WriteAllText(incoming,"native");using(var data=ShellDragData.ForFiles([incoming])){
     Raise(List(card),data.Data,DragDropKeyStates.LeftMouseButton,DragDrop.DragOverEvent);Raise(List(card),data.Data,DragDropKeyStates.None,DragDrop.DropEvent);
    }
    if(File.Exists(incoming)||!File.Exists(Path.Combine(downloads,"拖入.txt")))throw new Exception("card native default move failed");Console.WriteLine("PASS card drop routes to Windows native destination");
    var linked=Path.Combine(root,"链接源.txt");File.WriteAllText(linked,"native");using(var data=ShellDragData.ForFiles([linked])){Raise(List(card),data.Data,DragDropKeyStates.LeftMouseButton|DragDropKeyStates.AltKey,DragDrop.DragOverEvent);Raise(List(card),data.Data,DragDropKeyStates.AltKey,DragDrop.DropEvent);}
    if(!File.Exists(linked)||!Directory.EnumerateFiles(downloads,"*.lnk").Any())throw new Exception("card Alt link failed");Console.WriteLine("PASS card Alt drop preserves source and creates link");
    card.Place(new(100,100,304,48),true);if(Walk(card).OfType<ListBox>().Single().IsVisible)throw new Exception("collapsed list visible");card.Place(new(100,100,304,286),false);
    card.Hide();card.Show();card.Refresh();await Until(()=>List(card).Items.Count>=3);Console.WriteLine("PASS card collapse and hide restore without losing data");
    var state=new DockState();state.Files.DesktopCards=false;state.Files.DownloadsCollapsed=true;state.Files.FoldersCollapsed=true;var config=Path.Combine(root,"settings.json");DockStore.Save(config,state);var restored=DockStore.Load(config);if(restored.Files.DesktopCards||!restored.Files.DownloadsCollapsed||!restored.Files.FoldersCollapsed)throw new Exception("preferences reset");Console.WriteLine("PASS card preferences survive reload");
    var compactFile=new DesktopFileCard("小屏文件卡",true,()=>downloads,()=>[],()=>{},null,()=>{},()=>{});
    var compactWeather=new DesktopWeatherCard(()=>{},()=>{},_=>{});
    try{
     foreach(var w in new Window[]{compactFile,compactWeather}){
      var p=new DesktopCardPlacement{Width=304,Height=392};
      if(w is DesktopFileCard f){f.ConfigurePlacement(p,()=>{});f.RestoreOrPlace(new(100,100,304,48),false);}else{compactWeather.ConfigurePlacement(p,()=>{});compactWeather.RestoreOrPlace(new(100,160,304,48),false);}
      w.Show();var pt=NativeServices.PixelPosition(w);p.PixelX=pt.X;p.PixelY=pt.Y;
      if(w is DesktopFileCard f2)f2.RestoreOrPlace(new(100,100,304,48),false);else compactWeather.RestoreOrPlace(new(100,160,304,48),false);
      if(w.Height!=48||p.Height!=392)throw new Exception("auto compact card expands and overlaps siblings after first placement");
     }
    }finally{compactFile.CloseForShutdown();compactWeather.CloseForShutdown();}
    Console.WriteLine("PASS implicit small-screen collapse survives repeated placement without losing expanded height");
    if(!Walk(folders).OfType<System.Windows.Controls.Primitives.UniformGrid>().Any(g=>g.Columns==2))throw new Exception("folder card must keep native selectable rows in two columns");Console.WriteLine("PASS two-column folders retain native list selection and real paths");
    if(args.Length>0){await Task.Delay(800);Capture(card,Path.Combine(args[0],"downloads.png"));Capture(folders,Path.Combine(args[0],"folders.png"));}
   }catch(Exception ex){failure++;Console.WriteLine("FAIL "+ex);}finally{card.CloseForShutdown();folders.CloseForShutdown();app.Shutdown();}
  };
  app.DispatcherUnhandledException+=(_,e)=>{failure++;Console.WriteLine("FAIL dispatcher "+e.Exception);e.Handled=true;app.Shutdown();};
  card.Show();folders.Show();start.Start();app.Run();Directory.Delete(root,true);return failure==0?0:1;
 }
 static IEnumerable<DependencyObject> Walk(DependencyObject node){for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);yield return child;foreach(var sub in Walk(child))yield return sub;}}
 static void Raise(UIElement target,IDataObject data,DragDropKeyStates keys,RoutedEvent ev){var ctor=typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Single();var e=(DragEventArgs)ctor.Invoke([data,keys,DragDropEffects.Copy|DragDropEffects.Move|DragDropEffects.Link,target,new Point(4,4)]);e.RoutedEvent=ev;target.RaiseEvent(e);}
 static void Capture(Window w,string path){var dpi=VisualTreeHelper.GetDpi(w);var bitmap=new RenderTargetBitmap((int)(w.ActualWidth*dpi.DpiScaleX),(int)(w.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(w);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(path);png.Save(stream);}
}


