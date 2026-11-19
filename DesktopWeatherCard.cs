using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatingDock;

// Weather has its own lifetime, visibility and physical placement, just like the file cards.
public sealed partial class DesktopWeatherCard:Window
{
    readonly StackPanel content=new(){Margin=new Thickness(20,0,20,12)};
    readonly ScrollViewer body=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly TextBlock city=new(){FontSize=13,Foreground=GlassUi.Text,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    readonly Button collapse;
    bool closed,shutdown;
    public DesktopWeatherCard(Action toggle,Action hide,Action<FrameworkElement> menu)
    {
        Title="桌面天气";Width=380;Height=392;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;ShowInTaskbar=false;ShowActivated=false;Topmost=false;
        FontFamily=new FontFamily("Microsoft YaHei UI");SourceInitialized+=(_,_)=>DesktopLayer.Attach(this);
        var glass=new Grid();glass.Children.Add(new AcrylicMaterial(this,true,true));
        var shell=new DockPanel();var header=new DockPanel{Height=48,Margin=new Thickness(20,0,12,0)};
        var close=ActionButton("×",hide,"隐藏天气组件");DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        collapse=ActionButton("⌃",toggle,"折叠或展开天气组件");DockPanel.SetDock(collapse,Dock.Right);header.Children.Add(collapse);
        var settings=ActionButton("⋯",()=>{},"天气与城市");settings.Click+=(_,_)=>menu(settings);DockPanel.SetDock(settings,Dock.Right);header.Children.Add(settings);
        var title=new Grid{Background=Brushes.Transparent};var locationTitle=new DockPanel();locationTitle.Children.Add(new TextBlock{Text="\uE707",FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=16,Foreground=GlassUi.Text,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});locationTitle.Children.Add(city);title.Children.Add(locationTitle);title.Children.Add(GeometryHandle("CardMove",CardEdge.None,Cursors.SizeAll));header.Children.Add(title);
        DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);body.Content=content;body.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]=GlassUi.ScrollBarStyle();shell.Children.Add(body);glass.Children.Add(shell);
        Content=ResizableCard(new Border{CornerRadius=new CornerRadius(22),BorderThickness=new Thickness(0),Background=SystemParameters.HighContrast?SystemColors.WindowBrush:GlassUi.Color("#30132B3D"),Child=glass,ClipToBounds=true});
        Closing+=(_,e)=>{if(!shutdown){e.Cancel=true;hide();}};Closed+=(_,_)=>closed=true;
    }
    static Button ActionButton(string label,Action action,string name)
    {
        var b=new Button{Content=label,Width=28,Height=28,Foreground=GlassUi.Text,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Template=GlassUi.ButtonTemplate(),Cursor=Cursors.Hand,ToolTip=name};
        AutomationProperties.SetName(b,name);b.Click+=(_,_)=>action();return b;
    }
    static TextBlock Text(string value,double size=12)=>new(){Text=value,FontSize=size,Foreground=GlassUi.Text,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
    static string Value(double? value,string unit)=>value is double v&&double.IsFinite(v)?$"{v:0}{unit}":"—";
    public void Update(WeatherLocation location,WeatherReading? reading,bool failed)
    {
        city.Text=location.Name;city.ToolTip=location.Name;content.Children.Clear();
        var main=new Grid();main.ColumnDefinitions.Add(new());main.ColumnDefinitions.Add(new(){Width=new GridLength(82)});
        var temperature=Text(reading is null?"—°":$"{reading.Temperature:0}°",64);main.Children.Add(temperature);
        var icon=new Image{Source=WeatherArtwork.For(reading?.Code,reading?.IsDay??true),Width=70,Height=70,Stretch=Stretch.Uniform};Grid.SetColumn(icon,1);main.Children.Add(icon);content.Children.Add(main);
        var today=reading?.Daily?.FirstOrDefault();
        var condition=Text(reading is null?(failed?"天气暂不可用":"正在获取天气…"):WeatherText.Condition(reading.Code)+(today is null?"":$"  {today.Maximum:0}° / {today.Minimum:0}°"));condition.Margin=new Thickness(0,1,0,14);content.Children.Add(condition);
        var facts=new System.Windows.Controls.Primitives.UniformGrid{Columns=3,Margin=new Thickness(0,0,0,15)};
        foreach(var fact in new[]{"体感 "+Value(reading?.FeelsLike,"°"),"湿度 "+Value(reading?.Humidity,"%"),"风速 "+Value(reading?.WindSpeed," km/h")})facts.Children.Add(Text(fact,11));content.Children.Add(facts);
        var hours=reading?.Hourly?.Where(h=>h is not null&&h.Time>=DateTimeOffset.UtcNow.AddMinutes(-30)).Take(4).ToArray()??[];
        if(hours.Length>0){var hourly=new System.Windows.Controls.Primitives.UniformGrid{Columns=hours.Length,Margin=new Thickness(0,2,0,12)};foreach(var hour in hours){var column=new StackPanel();var time=Text(hour.Time.ToString("HH:mm"),11);time.HorizontalAlignment=HorizontalAlignment.Center;column.Children.Add(time);column.Children.Add(new Image{Source=WeatherArtwork.For(hour.Code,hour.IsDay),Width=28,Height=28,Margin=new Thickness(0,7,0,7)});var t=Text($"{hour.Temperature:0}°");t.HorizontalAlignment=HorizontalAlignment.Center;column.Children.Add(t);hourly.Children.Add(column);}content.Children.Add(hourly);}
        else{var missing=Text("逐小时预报暂不可用",11);missing.Margin=new Thickness(0,5,0,12);content.Children.Add(missing);}
        foreach(var day in reading?.Daily?.Where(d=>d is not null).Take(3)??[]){var row=new DockPanel{Margin=new Thickness(0,4,0,4)};var range=Text($"{day.Minimum:0}°  /  {day.Maximum:0}°",11);DockPanel.SetDock(range,Dock.Right);row.Children.Add(range);row.Children.Add(Text(day.Time.ToString("MM/dd")+"　"+WeatherText.Condition(day.Code),11));content.Children.Add(row);}
        bool stale = reading is not null && DateTimeOffset.UtcNow - reading.UpdatedAt > TimeSpan.FromMinutes(45);
        string status = failed ? "离线" : stale ? "旧数据" : reading is null ? "等待更新" : "已更新";
        string updatedAt = reading is null ? "" : " · " + reading.UpdatedAt.ToLocalTime().ToString("HH:mm");
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var lamp = new Border
        {
            Name = "WeatherStatusLamp", Width = 7, Height = 7, CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Background = SystemParameters.HighContrast ? SystemColors.WindowTextBrush :
                GlassUi.Color(failed ? "#D15D70" : stale ? "#D38E36" : reading is null ? "#5881CA" : "#46A876")
        };
        var caption = Text(status + updatedAt + " · Open-Meteo", 10);
        caption.Opacity = .78;
        caption.ToolTip = "每20分钟更新；更新时间为本机时间。点击标题右侧 ⋯ 刷新或更换城市。";
        footer.Children.Add(lamp);
        footer.Children.Add(caption);
        content.Children.Add(footer);
    }
    public void Place(DockArea area,bool collapsed)
    {
        geometryCollapsed=collapsed||area.Height<=48;Left=area.Left;Top=area.Top;Width=area.Width;Height=area.Height;body.Visibility=geometryCollapsed?Visibility.Collapsed:Visibility.Visible;collapse.Content=geometryCollapsed?"⌄":"⌃";
    }
    void RefreshLayout(){}
    public void CloseForShutdown(){shutdown=true;Close();}
}
