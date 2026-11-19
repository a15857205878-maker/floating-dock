using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FloatingDock;
public sealed partial class DesktopMusicCard:Window
{
 readonly Func<IMusicSession> factory;
 readonly Action openClient;
 readonly DockPanel body=new(){Margin=new Thickness(16,0,16,12)};
 readonly Image cover=new(){Width=56,Height=56,Stretch=Stretch.UniformToFill};
 readonly TextBlock note=new(){Text="\uE8D6",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=34,Foreground=GlassUi.Text,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
 readonly TextBlock track=new(){Text="网易云音乐",FontSize=14,Foreground=GlassUi.Text,TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock artist=new(){Text="打开网易云，选择一首音乐",FontSize=11,Foreground=GlassUi.Muted,Margin=new Thickness(0,6,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock stateText=new(){FontSize=10,Foreground=GlassUi.Muted,Margin=new Thickness(0,4,0,0),TextTrimming=TextTrimming.CharacterEllipsis};
 readonly TextBlock time=new(){Text="— / —",FontSize=10,Foreground=GlassUi.Muted,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};
 readonly Slider progress=new(){Name="MusicProgress",Minimum=0,Maximum=1,IsEnabled=false,IsMoveToPointEnabled=true,VerticalAlignment=VerticalAlignment.Center};
 readonly Button previous,toggle,next,collapse;
 readonly DispatcherTimer poll=new(){Interval=TimeSpan.FromSeconds(2)};
 IMusicSession? service;
 CancellationTokenSource? stop;
 MusicSnapshot current=new();
 bool closed,shutdown,busy,seeking;
 int generation;
 string lastCover="";
 public DesktopMusicCard(Func<IMusicSession> factory,Action openClient,Action fold,Action hide)
 {
  this.factory=factory;this.openClient=openClient;
  Title="网易云播放器";Width=304;Height=216;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=false;FontFamily=new FontFamily("Microsoft YaHei UI");
  SourceInitialized+=(_,_)=>DesktopLayer.Attach(this);
  var glass=new Grid();glass.Children.Add(new AcrylicMaterial(this,true,true));var shell=new DockPanel();
  var header=new DockPanel{Height=48,Margin=new Thickness(16,0,10,0)};
  var close=Button("×","隐藏网易云播放器",hide,28);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
  collapse=Button("⌃","折叠或展开网易云播放器",fold,28);DockPanel.SetDock(collapse,Dock.Right);header.Children.Add(collapse);
  var title=new Grid{Background=Brushes.Transparent};title.Children.Add(new TextBlock{Text="网易云音乐",FontSize=13,Foreground=GlassUi.Text,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,IsHitTestVisible=false});title.Children.Add(GeometryHandle("CardMove",CardEdge.None,Cursors.SizeAll));header.Children.Add(title);DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
  var controls=new DockPanel{Height=36};var open=Button("打开网易云 ↗","打开网易云音乐",Open,104);open.FontSize=11;DockPanel.SetDock(open,Dock.Right);controls.Children.Add(open);
  previous=Button("\uE892","上一首",()=>_ = CommandAsync("previous"),32);toggle=Button("\uE768","播放或暂停",()=>_ = CommandAsync("toggle"),38);next=Button("\uE893","下一首",()=>_ = CommandAsync("next"),32);
  foreach(var button in new[]{previous,toggle,next}){button.FontFamily=new FontFamily("Segoe Fluent Icons");button.FontSize=19;DockPanel.SetDock(button,Dock.Left);controls.Children.Add(button);}DockPanel.SetDock(controls,Dock.Bottom);body.Children.Add(controls);
  var timeline=new DockPanel{Height=28,Margin=new Thickness(0,4,0,0)};DockPanel.SetDock(time,Dock.Right);timeline.Children.Add(time);timeline.Children.Add(progress);AutomationProperties.SetName(progress,"网易云播放进度");DockPanel.SetDock(timeline,Dock.Bottom);body.Children.Add(timeline);
  var info=new DockPanel{Margin=new Thickness(0,3,0,0)};var art=new Grid{Width=56,Height=56,Margin=new Thickness(0,0,12,0)};art.Children.Add(note);art.Children.Add(cover);DockPanel.SetDock(art,Dock.Left);info.Children.Add(art);var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(track);labels.Children.Add(artist);labels.Children.Add(stateText);info.Children.Add(labels);body.Children.Add(info);var scroller=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};scroller.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=GlassUi.ScrollBarStyle();shell.Children.Add(scroller);glass.Children.Add(shell);
  Content=ResizableCard(new Border{CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(0),Background=SystemParameters.HighContrast?SystemColors.WindowBrush:GlassUi.Color("#30132B3D"),Child=glass,ClipToBounds=true});
  poll.Tick+=(_,_)=>QueueRefresh();IsVisibleChanged+=(_,_)=>{if(IsVisible){stop=new();service=factory();poll.Start();QueueRefresh();}else StopSession();};
  progress.PreviewMouseLeftButtonDown+=(_,_)=>seeking=true;
  progress.AddHandler(PreviewMouseLeftButtonUpEvent,new MouseButtonEventHandler((_,_)=>{if(seeking){seeking=false;_ = CommandAsync("seek",progress.Value);}}),true);
  progress.LostMouseCapture+=(_,_)=>seeking=false;
  progress.KeyUp+=(_,e)=>{if(e.Key is Key.Left or Key.Right or Key.Home or Key.End)_ = CommandAsync("seek",progress.Value);};
  Closing+=(_,e)=>{if(!shutdown){e.Cancel=true;hide();}};Closed+=(_,_)=>{closed=true;StopSession();};Apply(new());
 }
 static Button Button(string content,string name,Action action,double width){var b=new Button{Content=content,Width=width,Height=28,FontSize=17,Foreground=GlassUi.Text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Template=GlassUi.ButtonTemplate(),Cursor=Cursors.Hand,ToolTip=name};AutomationProperties.SetName(b,name);b.Click+=(_,_)=>action();return b;}
 void Open(){try{openClient();}catch(Exception ex){stateText.Text="暂时无法打开网易云";LifecycleLog.Write("music-open "+ex.GetType().Name);}}
 void QueueRefresh(){if(!closed&&!Dispatcher.HasShutdownStarted)_=Dispatcher.InvokeAsync(RefreshAsync);}
 async void RefreshAsync(){await QueryAsync(null,0);}
 Task CommandAsync(string command,double position=0)=>QueryAsync(command,position);
 async Task QueryAsync(string? command,double position)
 {
  if(closed||!IsVisible||busy||service is null||stop is null)return;
  var epoch=generation;var active=service;var token=stop.Token;busy=true;EnableControls();
  try{var snapshot=command is null?await active.ReadAsync(token):await active.CommandAsync(command,position,token);if(!closed&&IsVisible&&epoch==generation&&!token.IsCancellationRequested)Apply(snapshot);}
  catch(OperationCanceledException){}catch(Exception ex){if(!closed&&IsVisible&&epoch==generation){Apply(new());stateText.Text="等待网易云的系统播放信息";}LifecycleLog.Write("music-session "+ex.GetType().Name);}
  finally{if(epoch==generation){busy=false;EnableControls();}}
 }
 void Apply(MusicSnapshot snapshot)
 {
  current=snapshot;track.Text=snapshot.Available&&!string.IsNullOrWhiteSpace(snapshot.Title)?snapshot.Title:"网易云音乐";track.ToolTip=track.Text;
  var details=string.Join(" · ",new[]{snapshot.Artist,snapshot.Album}.Where(s=>!string.IsNullOrWhiteSpace(s)));
  artist.Text=snapshot.Available?(details.Length>0?details:"歌手信息未提供"):"打开客户端，播放后自动连接";artist.ToolTip=artist.Text;
  stateText.Text=!snapshot.Available?"等待网易云播放音乐":snapshot.State=="Playing"?"正在播放":snapshot.State=="Paused"?"已暂停":snapshot.State=="Stopped"?"已停止":"已连接网易云";
  stateText.ToolTip=snapshot.Available?stateText.Text:"网易云 → 设置 → 系统 → 开启SMTC；播放后会自动连接。";
  toggle.Content=snapshot.State=="Playing"?"\uE769":"\uE768";
  var hasTimeline=snapshot.Available&&snapshot.Duration>0;
  progress.Visibility=hasTimeline?Visibility.Visible:Visibility.Collapsed;
  time.Margin=hasTimeline?new Thickness(10,0,0,0):new Thickness(0);
  if(!seeking){progress.Maximum=Math.Max(1,snapshot.Duration);progress.Value=Math.Clamp(snapshot.Position,0,progress.Maximum);}
  time.Text=hasTimeline?Clock(snapshot.Position)+" / "+Clock(snapshot.Duration):snapshot.Available?"客户端未提供进度":"等待播放信息";
  time.ToolTip=hasTimeline?(snapshot.CanSeek?"拖动调整播放进度":"当前进度仅供查看"):time.Text;
  if(snapshot.Cover!=lastCover){lastCover=snapshot.Cover;cover.Source=null;try{if(lastCover.Length>0){using var stream=new MemoryStream(Convert.FromBase64String(lastCover));var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.StreamSource=stream;bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.DecodePixelWidth=128;bitmap.DecodePixelHeight=128;bitmap.EndInit();bitmap.Freeze();cover.Source=bitmap;}}catch(Exception ex)when(ex is FormatException or IOException or NotSupportedException or ArgumentException or InvalidOperationException){cover.Source=null;}}
  note.Visibility=cover.Source is null?Visibility.Visible:Visibility.Collapsed;EnableControls();
 }
 static string Clock(double seconds)=>TimeSpan.FromSeconds(Math.Clamp(double.IsFinite(seconds)?seconds:0,0,86400)).ToString(seconds>=3600?@"h\:mm\:ss":@"m\:ss");
 void EnableControls(){previous.IsEnabled=current.Available&&current.CanPrevious&&!busy;toggle.IsEnabled=current.Available&&current.CanToggle&&!busy;next.IsEnabled=current.Available&&current.CanNext&&!busy;progress.IsEnabled=current.Available&&current.Duration>0&&current.CanSeek&&!busy;}
 void StopSession(){poll.Stop();generation++;busy=false;stop?.Cancel();service?.Dispose();service=null;stop?.Dispose();stop=null;}
 public void Place(DockArea area,bool collapsed){geometryCollapsed=collapsed||area.Height<=48;Left=area.Left;Top=area.Top;Width=area.Width;Height=area.Height;body.Visibility=geometryCollapsed?Visibility.Collapsed:Visibility.Visible;collapse.Content=geometryCollapsed?"⌄":"⌃";}
 void RefreshLayout(){}
 public void CloseForShutdown(){shutdown=true;Close();}
}
