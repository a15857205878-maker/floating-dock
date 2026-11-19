using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FloatingDock;
public sealed partial class DockWindow
{
    DesktopFileCard? downloadCard,folderCard;
    void InitializeDesktopCards()
    {
        Loaded+=(_,_)=>Dispatcher.BeginInvoke(UpdateDesktopCards);
        LocationChanged+=(_,_)=>{PositionDesktopCards();PositionWeatherCard();};
        SizeChanged+=(_,_)=>{PositionDesktopCards();PositionWeatherCard();};
        Closed+=(_,_)=>{downloadCard?.CloseForShutdown();folderCard?.CloseForShutdown();};
    }
    void UpdateDesktopCards()
    {
        if(!IsLoaded)return;
        if(!state.Files.CardVisible(true)&&!state.Files.CardVisible(false)){downloadCard?.Hide();folderCard?.Hide();return;}
        if(downloadCard is null)
        {
            downloadCard=new DesktopFileCard("下载收件箱",true,FileLocations.Downloads,
                ()=>[],()=>ShowFilePanel("downloads",false,false),null,
                ()=>{state.Files.DownloadsCollapsed=!(state.Files.DownloadsCollapsed||state.Files.DownloadsPlacement.AutoCollapsed);state.Files.DownloadsPlacement.AutoCollapsed=false;Save();PositionDesktopCards();},()=>SetDesktopCardVisible(true,false));
            folderCard=new DesktopFileCard("常用文件夹",false,()=>"",CardFolders,
                ()=>ShowFilePanel("recent",false,false),()=>{AddFileFolder();folderCard?.Refresh();},
                ()=>{state.Files.FoldersCollapsed=!(state.Files.FoldersCollapsed||state.Files.FoldersPlacement.AutoCollapsed);state.Files.FoldersPlacement.AutoCollapsed=false;Save();PositionDesktopCards();},()=>SetDesktopCardVisible(false,false));
            downloadCard.ConfigurePlacement(state.Files.DownloadsPlacement,Save);
            folderCard.ConfigurePlacement(state.Files.FoldersPlacement,Save);
        }
        PositionDesktopCards();downloadCard.Refresh();folderCard!.Refresh();
    }
    void SetDesktopCardVisible(bool downloads,bool visible){state.Files.SetCardVisible(downloads,visible);Save();UpdateDesktopCards();}
    void AddDesktopCardMenus(ItemsControl menu)
    {
        foreach(bool downloads in new[]{true,false})
        {
            var toggle=Entry(downloads?"显示下载收件箱":"显示常用文件夹",()=>SetDesktopCardVisible(downloads,!state.Files.CardVisible(downloads)));
            toggle.IsCheckable=true;toggle.IsChecked=state.Files.CardVisible(downloads);menu.Items.Add(toggle);
        }
    }
    IReadOnlyList<string> CardFolders()=>new[]{Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),CollectionDirectory}
        .Concat(state.Files.AdditionalFolders).Where(p=>!string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    void PositionDesktopCards()
    {
        if(downloadCard is null||folderCard is null||!IsLoaded)return;
        var safe=NativeServices.WorkingArea(this);
        var positions=DockLayout.Widgets(new(safe.Left,safe.Top,safe.Width,safe.Height),new(Left,Top,Width,Height));
        if(state.Files.CardVisible(true))
        {
            downloadCard.RestoreOrPlace(positions.Downloads,state.Files.DownloadsCollapsed);
            if(positions.Downloads.Height>=48||state.Files.DownloadsPlacement.PixelX is not null)downloadCard.Show();else downloadCard.Hide();
            RememberInitialPlacement(downloadCard,state.Files.DownloadsPlacement);
        }
        else downloadCard.Hide();
        if(state.Files.CardVisible(false))
        {
            folderCard.RestoreOrPlace(positions.Folders,state.Files.FoldersCollapsed);
            if(positions.Folders.Height>=48||state.Files.FoldersPlacement.PixelX is not null)folderCard.Show();else folderCard.Hide();
            RememberInitialPlacement(folderCard,state.Files.FoldersPlacement);
        }
        else folderCard.Hide();
    }
}
