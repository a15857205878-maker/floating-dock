using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FloatingDock;

public sealed partial class DockWindow
{
    IslandLayout? cachedIslands;
    double cachedIslandWidth;
    IslandLayout Islands {get {if(cachedIslands is null||cachedIslandWidth!=Width){cachedIslandWidth=Width;cachedIslands=DockLayout.LauncherWidth(Width);}return cachedIslands;}}
    string GroupCaption=>"\uE80A";
    readonly Border groupSeparator=new(){Width=1,Height=28,Background=Brush("#38FFFFFF"),IsHitTestVisible=false};
    readonly Border systemSeparator=new(){Width=1,Height=28,Background=Brush("#38FFFFFF"),IsHitTestVisible=false};
    readonly List<Border> systemReflections=[];
    Border groupReflection=null!;
    readonly Border infoIsland=new(){Name="InfoIsland"};
    readonly Border toolsIsland=new(){Name="ToolsIsland"};
    Button desktopButton=null!,groupButton=null!,searchButton=null!,downloadsButton=null!;
    void InitializeIslands()
    {
        foreach(var island in new[]{infoIsland,toolbarBackground,toolsIsland})
        {
            island.CornerRadius=new CornerRadius(18);island.Background=Brush("#08101823");island.BorderBrush=Brushes.Transparent;island.BorderThickness=new Thickness(0);
            island.Effect=new DropShadowEffect{Color=Colors.Black,BlurRadius=8,ShadowDepth=1,Opacity=.05};
            Panel.SetZIndex(island,-1);
            if(!ReferenceEquals(island,toolbarBackground)){island.Child=new AcrylicMaterial(this,true);root.Children.Add(island);}
        }
        desktopButton=Control("\uE7F4","显示桌面",32);desktopButton.FontFamily=new FontFamily("Segoe Fluent Icons");desktopButton.FontSize=20;
        desktopButton.Click+=(_,_)=>Try(DesktopLauncher.ShowDesktop);
        groupButton=Control("全部 ⌄","应用分组",64);groupButton.FontSize=11;
        groupButton.Click+=(_,_)=>{
            var context = new ContextMenu();
            AddGroupChoices(context);
            OpenMenu(groupButton, context);
        };
        searchButton=Control("\uE721","搜索应用或文件",32);searchButton.FontFamily=new FontFamily("Segoe Fluent Icons");searchButton.FontSize=20;
        searchButton.Click+=(_,_)=>ShowFilePanel("recent",true,true);
        downloadsButton=Control("\uE896","最近下载",32);downloadsButton.FontFamily=new FontFamily("Segoe Fluent Icons");downloadsButton.FontSize=21;
        downloadsButton.Click+=(_,_)=>ShowFilePanel("downloads",false,false);
        foreach(var control in new[]{desktopButton,groupButton,searchButton,downloadsButton})root.Children.Add(control);
        root.Children.Add(groupSeparator);root.Children.Add(systemSeparator);
        groupButton.FontFamily=new FontFamily("Segoe Fluent Icons");groupButton.Background=PlateFill("#D0314B5D","#A5223B4C");groupButton.Template=TransparentButtonTemplate(14);groupButton.Effect=null;
        groupReflection=FixedIconMirror(groupButton,"CategoryIconReflection");root.Children.Add(groupReflection);
        for(int i=0;i<systemButtons.Length;i++){
            var system=systemButtons[i];system.Template=TransparentButtonTemplate(14);system.Effect=null;
            system.Background=i switch{0=>PlateFill("#C0B5DFEF","#985A95BB"),1=>PlateFill("#D4DCECEF","#A8A1C0CD"),_=>PlateFill("#BC96BDD5","#985E8EAD")};
            system.Content=new TextBlock{Text=SystemEntries[i].Glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=28,Foreground=i==1?Brush("#FF326381"):Brush("#FFF2F8FD"),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
            var mirror=FixedIconMirror(system,"SystemIconReflection");
            systemReflections.Add(mirror);root.Children.Add(mirror);
        }
        add.Visibility=Visibility.Collapsed;
    }
    static Brush PlateFill(string top,string bottom)=>new LinearGradientBrush(Brush(top).Color,Brush(bottom).Color,90);
    static Border FixedIconMirror(Button button,string name)=>new(){Name=name,Width=56,Height=56,Background=new VisualBrush(button){Stretch=Stretch.Fill},IsHitTestVisible=false,Opacity=.20,RenderTransformOrigin=new Point(.5,.5),RenderTransform=new ScaleTransform(1,-1),Clip=new RectangleGeometry(new Rect(0,35,56,21)),OpacityMask=new LinearGradientBrush(Colors.Transparent,Colors.Black,90)};
    void LayoutIslands()
    {
        var layout=Islands;
        infoIsland.Visibility=toolsIsland.Visibility=Visibility.Collapsed;
        toolbarBackground.Width=Math.Max(1,Width-16);toolbarBackground.Height=92;Set(toolbarBackground,8,84);toolbarBackground.CornerRadius=new CornerRadius(26);
        strip.Width=Viewport;strip.Height=176;Set(strip,LeftInset,0);strip.Clip=new RectangleGeometry(new Rect(0,0,Viewport,176));stripHit.Width=Viewport;stripHit.Height=92;Set(stripHit,0,84);
        grip.Width=14;grip.Height=32;grip.FontSize=12;Set(grip,12,108);
        desktopButton.Visibility=weatherButton.Visibility=searchButton.Visibility=filesButton.Visibility=downloadsButton.Visibility=Visibility.Collapsed;
        groupButton.Width=layout.GroupWidth;groupButton.Height=56;groupButton.Content=GroupCaption;groupButton.ToolTip="应用分类 · "+DockGroups.Name(state.SelectedGroup);Set(groupButton,38,94);groupButton.FontSize=28;
        groupReflection.Width=layout.GroupWidth;groupReflection.Clip=new RectangleGeometry(new Rect(0,35,layout.GroupWidth,21));Set(groupReflection,38,152);
        groupSeparator.Visibility=Width>=420?Visibility.Visible:Visibility.Collapsed;Set(groupSeparator,38+layout.GroupWidth+10,110);
        systemSeparator.Visibility=Width>=700?Visibility.Visible:Visibility.Collapsed;Set(systemSeparator,layout.Tools.Left-10,110);
        foreach(var system in systemButtons)system.Visibility=Width>=700?Visibility.Visible:Visibility.Collapsed;
        int index=0;foreach(var system in systemButtons){system.Width=system.Height=56;Set(system,layout.Tools.Left+68*index++,94);}
        organizeButton.Visibility=Width>=300?Visibility.Visible:Visibility.Collapsed;
        organizeButton.Width=40;organizeButton.Height=32;Set(organizeButton,layout.Tools.Right-79,108);
        menu.Width=32;menu.Height=32;Set(menu,layout.Tools.Right-34,108);
        ApplyTileLanes();
        PositionDockHandle();
        for(int i=0;i<systemReflections.Count;i++){systemReflections[i].Visibility=Width>=700?Visibility.Visible:Visibility.Collapsed;Set(systemReflections[i],layout.Tools.Left+68*i,152);}
        weatherDivider.Visibility=systemDivider.Visibility=fileDivider.Visibility=Visibility.Collapsed;
        LayoutWeather();Canvas.SetLeft(empty,8);Canvas.SetTop(empty,112);empty.FontSize=11;empty.MaxWidth=Math.Max(40,Viewport-16);

    }
    void ShowFilePanel(string source,bool focusSearch,bool applications)
    {
        if(fileTransferBusy||fileDraggingOut)return;
        searchApplications=applications;todayFilesOnly=false;fileNavigation.Select(source);
        fileChanging=true;collectionSearch.Text="";fileChanging=false;
        if(!collectionPopup.IsOpen)ToggleCollection();else{ClearFileResults();StartFileWatching();PopulateCollection();}
        if(focusSearch){collectionSearch.Focus();collectionSearch.SelectAll();}
    }
    void AddGroupChoices(ItemsControl menu, DockItem? item = null)
    {
        var selected = item?.Group ?? state.SelectedGroup;
        var groups = item is null ? DockGroupIds.Values.Prepend("all") : DockGroupIds.Values;
        foreach (var id in groups)
        {
            var option = Entry(DockGroups.Name(id), () =>
            {
                if (id == (item?.Group ?? state.SelectedGroup)) return;
                if (item is null)
                {
                    state.SelectedGroup = id;
                    scroll = targetScroll = 0;
                }
                else item.Group = id;
                Rebuild();
                if (item is null) Layout();
                Save();
            });
            option.IsCheckable = true;
            option.IsChecked = id == selected;
            if (item is null)
                option.ToolTip = (id == "all" ? state.Items.Count : state.Items.Count(entry => entry.Group == id)) + " 个入口";
            menu.Items.Add(option);
        }
    }
}

