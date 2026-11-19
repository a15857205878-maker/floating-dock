using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace FloatingDock;

public sealed partial class DockWindow
{
    bool searchApplications,todayFilesOnly;
    TextBlock fileHeading=null!;
    Button todayFilter=null!,revealFileButton=null!,fileSortButton=null!,desktopCategoryButton=null!;
    DesktopFileCategory desktopCategory;
    static readonly (DesktopFileCategory Category, string Label)[] DesktopCategories =
    [
        (DesktopFileCategory.All,"全部类型"), (DesktopFileCategory.Folders,"文件夹"),
        (DesktopFileCategory.Documents,"文档"), (DesktopFileCategory.Images,"图片"),
        (DesktopFileCategory.Media,"音视频"), (DesktopFileCategory.Archives,"压缩包"),
        (DesktopFileCategory.Shortcuts,"快捷方式"), (DesktopFileCategory.Other,"其他")
    ];
    Border fileSidebarBorder=null!;
    readonly StackPanel fileSidebar=new(){Name="FileSidebar",Margin=new Thickness(8,12,8,10)};
    Grid fileHeaders=null!;
    Grid fileNavigationBar=null!;
    TextBlock fileSearchHint=null!;
    string fileSidebarSignature="";

    void BuildFilePanel()
    {
        var glass=new Grid();glass.Children.Add(new AcrylicMaterial(this,true,true));
        var shell=new DockPanel();
        var heading=new DockPanel{Margin=new Thickness(16,12,12,10)};
        foreach(var pair in new[]{("×","收起文件"),("⋯","文件收纳菜单"),("↻","刷新文件")})
        {
            var action=FileAction(pair.Item1,()=>{},30);AutomationProperties.SetName(action,pair.Item2);action.FontSize=18;DockPanel.SetDock(action,Dock.Right);heading.Children.Add(action);
            action.Click+=(_,_)=>{if(pair.Item2=="收起文件")CloseCollection();else if(pair.Item2=="刷新文件")PopulateCollection();else OpenMenu(action,FileBrowserMenu());};
        }
        heading.Children.Add(new TextBlock{Text="文件",Foreground=GlassUi.Text,FontSize=17,VerticalAlignment=VerticalAlignment.Center});DockPanel.SetDock(heading,Dock.Top);shell.Children.Add(heading);
        var navigation=new Grid{Margin=new Thickness(16,0,16,12)};
        fileNavigationBar=navigation;
        foreach(var width in new[]{GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),new GridLength(220)})navigation.ColumnDefinitions.Add(new(){Width=width});
        fileBack=FileAction("←",()=>NavigateFiles(fileNavigation.Back),32);AutomationProperties.SetName(fileBack,"返回");navigation.Children.Add(fileBack);
        fileUp=FileAction("↑",()=>NavigateFiles(fileNavigation.Up),32);AutomationProperties.SetName(fileUp,"上一级");Grid.SetColumn(fileUp,1);navigation.Children.Add(fileUp);
        fileLocation=new TextBlock{Foreground=GlassUi.Text,FontSize=12,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
        var breadcrumb=new Border{Background=Brush("#18FFFFFF"),CornerRadius=new CornerRadius(6),Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,10,0),Child=fileLocation};Grid.SetColumn(breadcrumb,2);navigation.Children.Add(breadcrumb);
        collectionSearch=new TextBox{Name="CollectionSearch",Padding=new Thickness(8,6,8,6),FontSize=12,Background=Brush("#18FFFFFF"),Foreground=GlassUi.Text,CaretBrush=GlassUi.Text,BorderBrush=Brush("#385D7596"),ToolTip="输入文件名；独立搜索入口同时查找应用与已登记目录"};
        AutomationProperties.SetName(collectionSearch,"搜索文件名");
        collectionSearch.TextChanged+=(_,_)=>{if(fileChanging)return;CancelFileQuery();fileSearchDelay.Stop();fileSearchDelay.Start();};
        var searchContainer=new Grid();searchContainer.Children.Add(collectionSearch);
        fileSearchHint=new TextBlock{Text="搜索文件名",Foreground=Brush("#AEBACCDD"),FontSize=12,Margin=new Thickness(9,0,0,0),VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};searchContainer.Children.Add(fileSearchHint);
        collectionSearch.TextChanged+=(_,_)=>fileSearchHint.Visibility=collectionSearch.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumn(searchContainer,3);navigation.Children.Add(searchContainer);DockPanel.SetDock(navigation,Dock.Top);shell.Children.Add(navigation);
        AddQuickDropPanel(shell);
        var footer=new DockPanel{Margin=new Thickness(16,9,16,12)};
        revealFileButton=FileAction("打开所在位置",RevealFileSelection,118);DockPanel.SetDock(revealFileButton,Dock.Right);footer.Children.Add(revealFileButton);
        collectionStatus=new TextBlock{Foreground=GlassUi.Muted,FontSize=11,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};
        footer.Children.Add(collectionStatus);DockPanel.SetDock(footer,Dock.Bottom);shell.Children.Add(footer);
        var content=new DockPanel();
        var sidebarScroll=new ScrollViewer{Content=fileSidebar,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        sidebarScroll.Resources[typeof(ScrollBar)]=GlassUi.ScrollBarStyle();
        fileSidebarBorder=new Border{Width=142,Background=Brush("#14233248"),BorderBrush=Brush("#183F536F"),BorderThickness=new Thickness(0,0,1,0),Child=sidebarScroll};DockPanel.SetDock(fileSidebarBorder,Dock.Left);content.Children.Add(fileSidebarBorder);
        var body=new DockPanel{Margin=new Thickness(14,8,14,4)};
        var section=new DockPanel{Margin=new Thickness(0,0,0,10)};
        fileSortButton=FileAction("最近修改 ⌄",()=>{},120);fileSortButton.FontSize=11;AutomationProperties.SetName(fileSortButton,"文件排序");
        fileSortButton.Click+=(_,_)=>{
            var menu=new ContextMenu();foreach(bool byName in new[]{false,true}){var entry=Entry(byName?"按名称（文件夹优先）":"最近修改优先",()=>{state.Files.NameSort=byName;Save();PopulateCollection();});entry.IsCheckable=true;entry.IsChecked=state.Files.NameSort==byName;menu.Items.Add(entry);}OpenMenu(fileSortButton,menu);
        };
        DockPanel.SetDock(fileSortButton,Dock.Right);section.Children.Add(fileSortButton);fileHeading=new TextBlock{Foreground=GlassUi.Text,FontSize=18,VerticalAlignment=VerticalAlignment.Center};section.Children.Add(fileHeading);DockPanel.SetDock(section,Dock.Top);body.Children.Add(section);
        var filters=new WrapPanel{Margin=new Thickness(0,0,0,8)};
        desktopCategoryButton=FileAction("全部类型 ⌄",ShowDesktopCategories,90);
        AutomationProperties.SetName(desktopCategoryButton,"桌面文件分类");
        filters.Children.Add(desktopCategoryButton);
        todayFilter=FileAction("今天新增",()=>{todayFilesOnly=!todayFilesOnly;PopulateCollection();},80);
        todayFilter.ToolTip="按文件创建时间筛选今天新增的项目";
        filters.Children.Add(todayFilter);DockPanel.SetDock(filters,Dock.Top);body.Children.Add(filters);
        fileHeaders=DetailsGrid();fileHeaders.Name="FileDetailsHeaders";fileHeaders.Margin=new Thickness(7,2,19,6);
        foreach(var pair in new[]{("名称",0),("修改时间",1),("类型",2),("大小",3)}){var label=new TextBlock{Text=pair.Item1,Foreground=GlassUi.Muted,FontSize=11,HorizontalAlignment=pair.Item2==3?HorizontalAlignment.Right:HorizontalAlignment.Left};Grid.SetColumn(label,pair.Item2);fileHeaders.Children.Add(label);}
        DockPanel.SetDock(fileHeaders,Dock.Top);body.Children.Add(fileHeaders);
        collectionItems.Name="CollectionItems";
        fileScroll=new ScrollViewer{Content=collectionItems,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};fileScroll.Resources[typeof(ScrollBar)]=GlassUi.ScrollBarStyle();body.Children.Add(fileScroll);content.Children.Add(body);shell.Children.Add(content);glass.Children.Add(shell);
        quickDropFullSections = [heading, navigation, footer, content];
        collectionSurface=new Border{Name="CollectionSurface",Focusable=true,Width=860,MaxHeight=640,Background=Brush("#54142132"),BorderBrush=Brush("#185D718D"),BorderThickness=new Thickness(.5),CornerRadius=new CornerRadius(18),Child=glass};
        System.Windows.Documents.TextElement.SetFontFamily(collectionSurface,new FontFamily("Microsoft YaHei UI"));System.Windows.Documents.TextElement.SetFontSize(collectionSurface,12);
        collectionPopup=new Popup{Child=collectionSurface,AllowsTransparency=true,StaysOpen=false,PlacementTarget=filesButton,Placement=PlacementMode.Custom,PopupAnimation=SystemParameters.ClientAreaAnimation?PopupAnimation.Fade:PopupAnimation.None};
        collectionPopup.CustomPopupPlacementCallback=(size,target,offset)=>{
            var area=NativeServices.WorkingArea(this);var anchor=((FrameworkElement)collectionPopup.PlacementTarget).TranslatePoint(new Point(),this);
            var place=DockLayout.Panel(new(area.Left,area.Top,area.Width,area.Height),Left+anchor.X,Top+anchor.Y,target.Width,target.Height,size.Width,size.Height);
            return [new(new Point(place.Left-Left-anchor.X,place.Top-Top-anchor.Y),PopupPrimaryAxis.None)];
        };
    }
    Grid DetailsGrid()
    {
        bool narrow=collectionSurface is not null&&collectionSurface.Width<700,small=collectionSurface is not null&&collectionSurface.Width<500;
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(small?0:narrow?96:120)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(narrow?0:90)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(narrow?0:70)});return grid;
    }
    void UpdateFileSidebar()
    {
        var signature=string.Join('|',fileNavigation.Sources.Select(s=>s.Id+":"+s.Name));
        if(signature!=fileSidebarSignature)
        {
            fileSidebarSignature=signature;fileSidebar.Children.Clear();fileSourceButtons.Clear();
            foreach(var pair in new[]{("recent","最近文件","\uE823"),("downloads","下载","\uE896"),("desktop","桌面收件箱","\uE7F4"),("archive","收纳","\uE8B7")})AddSidebarEntry(pair.Item1,pair.Item2,pair.Item3);
            if(fileNavigation.Sources.Any(s=>s.Id.StartsWith("folder:",StringComparison.Ordinal)))fileSidebar.Children.Add(new TextBlock{Text="常用位置",Foreground=GlassUi.Muted,FontSize=10,Margin=new Thickness(10,18,0,6)});
            foreach(var source in fileNavigation.Sources.Where(s=>s.Id.StartsWith("folder:",StringComparison.Ordinal)))AddSidebarEntry(source.Id,source.Name,"\uE8B7");
            var addFolder=FileAction("＋ 常用位置",AddFileFolder,70);AutomationProperties.SetName(addFolder,"添加常用文件夹");addFolder.Foreground=GlassUi.Muted;addFolder.FontSize=10;addFolder.Margin=new Thickness(0,12,0,0);fileSidebar.Children.Add(addFolder);
        }
        fileSidebarBorder.Width=collectionSurface.Width<650?110:142;
        foreach (var (id, button) in fileSourceButtons)
        {
            bool selected = id == fileNavigation.SelectedId;
            button.Background = Brush(selected ? "#70466591" : "#01000000");
            button.BorderBrush = selected
                ? SystemParameters.HighContrast ? SystemColors.HighlightBrush : Brush("#A09EC6FF")
                : Brushes.Transparent;
            AutomationProperties.SetItemStatus(button, selected ? "当前来源" : "");
        }
    }
    void AddSidebarEntry(string id,string name,string glyph)
    {
        var button=FileAction(name,()=>NavigateFiles(()=>fileNavigation.Select(id)));button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Padding=new Thickness(9,8,7,8);button.Margin=new Thickness(0,0,0,3);button.ToolTip=fileNavigation.Sources.FirstOrDefault(s=>s.Id==id)?.Path??"已登记位置的最近文件";
        button.Tag=fileNavigation.Sources.FirstOrDefault(source=>source.Id==id);
        button.BorderThickness = new Thickness(2, 0, 0, 0);
        var label=new DockPanel();label.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=17,Foreground=GlassUi.Text,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center});label.Children.Add(new TextBlock{Text=name=="桌面收件箱"?"桌面":name,FontSize=12,Foreground=GlassUi.Text,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center});button.Content=label;
        fileSourceButtons[id]=button;fileSidebar.Children.Add(button);
    }
    void RevealFileSelection()
    {
        var selected=collectionItems.Children.OfType<Button>().Select(b=>(BrowserEntry)b.Tag).Where(e=>fileSelection.Contains(e.Path)).ToArray();
        if(selected.Length==1){CloseCollection();Try(()=>NativeServices.Reveal(selected[0].Path));}
        else if(!fileNavigation.IsRecent){CloseCollection();Try(()=>NativeServices.Open(fileNavigation.CurrentDirectory));}
    }
    void UpdateRevealFileButton()
    {
        revealFileButton.IsEnabled=fileSelection.Count==1||(!fileNavigation.IsRecent&&fileSelection.Count==0);
        revealFileButton.Content=fileSelection.Count==0?"打开当前目录":"打开所在位置";
    }
    void UpdatePanelHeader()
    {
        UpdateFileSidebar();
        UpdateQuickDropPanel();
        fileHeading.Text=searchApplications?"搜索应用与文件":fileNavigation.IsRecent?"最近文件":fileNavigation.Source?.Name??"文件";
        fileSortButton.Content=state.Files.NameSort?"按名称 ⌄":"最近修改 ⌄";todayFilter.Background=Brush(todayFilesOnly?"#605676A3":"#14FFFFFF");
        fileSortButton.Visibility=collectionSurface.Width<520?Visibility.Collapsed:Visibility.Visible;
        desktopCategoryButton.Visibility=fileNavigation.SelectedId=="desktop"?Visibility.Visible:Visibility.Collapsed;
        desktopCategoryButton.Content=DesktopCategories.First(item=>item.Category==desktopCategory).Label+" ⌄";
        fileNavigationBar.ColumnDefinitions[3].Width=new GridLength(collectionSurface.Width<600?Math.Max(100,collectionSurface.Width*.34):220);
        var columns=DetailsGrid();for(int i=0;i<4;i++)fileHeaders.ColumnDefinitions[i].Width=columns.ColumnDefinitions[i].Width;
        foreach(UIElement label in fileHeaders.Children)label.Visibility=fileHeaders.ColumnDefinitions[Grid.GetColumn(label)].Width.Value==0?Visibility.Collapsed:Visibility.Visible;
        fileSearchHint.Text=searchApplications?"搜索应用或文件":"搜索文件名";
        fileLocation.ToolTip=fileNavigation.IsRecent?"桌面、下载、收纳及常用目录":fileNavigation.CurrentDirectory;
        string relative=fileNavigation.Source is {} source?Path.GetRelativePath(source.Path,fileNavigation.CurrentDirectory):"";
        fileLocation.Text=searchApplications?"应用与常用文件":fileNavigation.IsRecent?"全部常用位置":(fileNavigation.Source?.Name??"文件")+(relative=="."?"":" › "+relative.Replace(Path.DirectorySeparatorChar.ToString()," › "));
        UpdateRevealFileButton();
    }
    void ShowDesktopCategories()
    {
        var menu=new ContextMenu();
        foreach(var (category,label) in DesktopCategories)
        {
            var item=Entry(label,()=>
            {
                desktopCategory=category;
                ClearFileResults();
                fileScroll.ScrollToTop();
                PopulateCollection();
            });
            item.IsCheckable=true;
            item.IsChecked=category==desktopCategory;
            menu.Items.Add(item);
        }
        OpenMenu(desktopCategoryButton,menu);
    }

    string EntryType(BrowserEntry entry)=>entry.Kind.Length>0?entry.Kind:entry.IsDirectory?"文件夹":Path.GetExtension(entry.Path).ToLowerInvariant() switch{".pdf"=>"PDF 文档",".docx" or ".doc"=>"Word 文档",".zip" or ".7z" or ".rar"=>"压缩文件",".png" or ".jpg" or ".jpeg"=>"图片",var ext=>ext.Length>0?ext[1..].ToUpperInvariant()+" 文件":"文件"};
    Grid FileRowContent(BrowserEntry entry,Image image)
    {
        var grid=DetailsGrid();grid.Margin=new Thickness(7,6,7,6);
        var name=new DockPanel{Margin=new Thickness(0,0,12,0)};image.Width=image.Height=28;image.Margin=new Thickness(0,0,10,0);DockPanel.SetDock(image,Dock.Left);name.Children.Add(image);
        var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(new TextBlock{Text=entry.Name,Foreground=GlassUi.Text,FontSize=12,TextTrimming=TextTrimming.CharacterEllipsis});
        if(fileNavigation.IsRecent||!string.IsNullOrWhiteSpace(collectionSearch.Text))labels.Children.Add(new TextBlock{Text=Path.GetDirectoryName(entry.Path),Foreground=GlassUi.Muted,FontSize=10,TextTrimming=TextTrimming.CharacterEllipsis});
        name.Children.Add(labels);grid.Children.Add(name);
        string size=entry.Kind=="应用"||entry.IsDirectory?"—":entry.Size>=1048576?$"{entry.Size/1048576d:0.#} MB":entry.Size>=1024?$"{entry.Size/1024d:0.#} KB":$"{entry.Size} B";
        foreach(var pair in new[]{(entry.ModifiedUtc==DateTime.MinValue?"—":entry.ModifiedUtc.ToLocalTime().ToString("MM-dd HH:mm"),1),(EntryType(entry),2),(size,3)}){var label=new TextBlock{Text=pair.Item1,Foreground=GlassUi.Muted,FontSize=11,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=pair.Item2==3?HorizontalAlignment.Right:HorizontalAlignment.Left,Margin=new Thickness(0,0,pair.Item2==3?0:8,0)};Grid.SetColumn(label,pair.Item2);grid.Children.Add(label);}
        return grid;
    }
}
