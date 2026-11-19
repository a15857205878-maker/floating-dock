using System.Windows;
using System.Windows.Controls;

namespace FloatingDock;
public sealed partial class DockWindow
{
 readonly Func<IMusicSession> musicFactory;
 DesktopMusicCard? musicCard;
 void InitializeMusic()
 {
  Loaded+=(_,_)=>Dispatcher.BeginInvoke(UpdateMusicCard);
  LocationChanged+=(_,_)=>PositionMusicCard();SizeChanged+=(_,_)=>PositionMusicCard();
  Closed+=(_,_)=>musicCard?.CloseForShutdown();
 }
 void UpdateMusicCard()
 {
  if(!loaded)return;
  if(!state.MusicWidget.Visible){musicCard?.Hide();return;}
  if(musicCard is null){
   musicCard=new DesktopMusicCard(musicFactory,NetEaseClient.Open,()=>{state.MusicWidget.Collapsed=!(state.MusicWidget.Collapsed||state.MusicWidget.Placement.AutoCollapsed);state.MusicWidget.Placement.AutoCollapsed=false;Save();PositionMusicCard();},()=>SetMusicVisible(false));
   musicCard.ConfigurePlacement(state.MusicWidget.Placement,Save);
  }
  PositionMusicCard();
 }
 void SetMusicVisible(bool visible){state.MusicWidget.Visible=visible;Save();UpdateMusicCard();}
 void PositionMusicCard()
 {
  if(musicCard is null||!loaded||!state.MusicWidget.Visible)return;
  var bounds=NativeServices.WorkingArea(this);var occupied=new List<DockArea>();
  foreach(var widget in new Window?[]{weatherCard,downloadCard,folderCard})if(widget?.IsVisible==true)occupied.Add(new(widget.Left,widget.Top,widget.Width,widget.Height));
  var fallback=DockLayout.MusicCard(new(bounds.Left,bounds.Top,bounds.Width,bounds.Height),new(Left,Top,Width,Height),occupied);
  if(fallback.Height<48&&state.MusicWidget.Placement.PixelX is null){musicCard.Hide();return;}
  musicCard.RestoreOrPlace(fallback,state.MusicWidget.Collapsed);musicCard.Show();RememberInitialPlacement(musicCard,state.MusicWidget.Placement);
 }
 void AddMusicMenu(ItemsControl menu)
 {
  var visible=Entry("显示网易云播放器",()=>SetMusicVisible(!state.MusicWidget.Visible));visible.IsCheckable=true;visible.IsChecked=state.MusicWidget.Visible;menu.Items.Add(visible);
  menu.Items.Add(Entry("打开网易云音乐",()=>Try(NetEaseClient.Open)));
 }
}
