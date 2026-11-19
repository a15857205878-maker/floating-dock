using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DockWindow
{
    WeatherService weatherService = null!;
    Button weatherButton = null!;
    readonly DockPanel weatherContent = new() { LastChildFill = true };
    readonly Image weatherSymbol = new() { Width = 28, Height = 28, Margin = new Thickness(0, 0, 9, 0), Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock weatherText = new() { FontSize = 12, Foreground = Brush("#F1F5FC"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer weatherTimer = new() { Interval = TimeSpan.FromMinutes(20) };
    readonly CancellationTokenSource weatherLifetime = new();
    CancellationTokenSource? weatherRequest;
    DesktopWeatherCard? weatherCard;
    bool weatherFailed;
    int weatherFailures;

    void InitializeWeather(WeatherService service)
    {
        weatherService = service;
        weatherButton = Control("", "天气与城市", 300);
        weatherContent.Children.Add(weatherSymbol); weatherContent.Children.Add(weatherText);
        weatherButton.Content = weatherContent;
        weatherButton.Click += (_, _) => OpenMenu(weatherButton, WeatherMenu());
        root.Children.Add(weatherButton);
        weatherTimer.Tick += async (_, _) => await RefreshWeatherAsync();
        UpdateWeatherLabel(false);
    }

    void StartWeather() { EnsureWidgetLayout();UpdateWeatherCard();weatherTimer.Start(); _ = RefreshWeatherAsync(); }
    void StopWeather() { weatherTimer.Stop(); weatherLifetime.Cancel(); weatherRequest?.Cancel();weatherCard?.CloseForShutdown(); }
    void LayoutWeather() { weatherContent.MaxWidth = Math.Max(40,weatherButton.Width-8); weatherText.MaxWidth = Math.Max(24, weatherButton.Width-45); }

    void UpdateWeatherLabel(bool failed)
    {
        weatherFailed=failed;
        var reading = state.Weather?.Location == state.Location ? state.Weather : null;
        weatherSymbol.Source = WeatherArtwork.For(reading?.Code, reading?.IsDay ?? true);
        weatherText.Text = reading is null ? state.Location.Name + (failed ? " · 离线" : " · 更新中") : state.Location.Name + " " + Math.Round(reading.Temperature).ToString("0") + "°" + (failed ? " · 离线" : DateTimeOffset.UtcNow-reading.UpdatedAt>TimeSpan.FromMinutes(45)?" · 旧数据":"");
        var last = reading is null ? "尚未获取天气" : "更新于 " + reading.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm") + "（本机时间）";
        weatherButton.ToolTip = (reading is null ? "" : WeatherText.Summary(reading, DateTimeOffset.UtcNow)+"\n") + last + "\n每20分钟更新 · 点击刷新或更换城市\n天气数据：Open-Meteo";
        weatherText.MaxWidth = Math.Max(24, weatherButton.Width - 45);
        weatherCard?.Update(state.Location,reading,failed);
    }

    void EnsureWidgetLayout()
    {
        if(state.WidgetLayoutVersion>=1)return;
        // The approved template is applied once. Hidden preferences and all launch entries survive.
        WidgetTemplate.Apply(state);Save();
    }
    void UpdateWeatherCard()
    {
        if(!loaded)return;
        if(!state.WeatherWidget.Visible){weatherCard?.Hide();return;}
        if(weatherCard is null){weatherCard=new DesktopWeatherCard(()=>{state.WeatherWidget.Collapsed=!(state.WeatherWidget.Collapsed||state.WeatherWidget.Placement.AutoCollapsed);state.WeatherWidget.Placement.AutoCollapsed=false;Save();PositionWeatherCard();},()=>SetWeatherVisible(false),owner=>OpenMenu(owner,WeatherMenu()));weatherCard.ConfigurePlacement(state.WeatherWidget.Placement,Save);}
        weatherCard.Update(state.Location,state.Weather?.Location==state.Location?state.Weather:null,weatherFailed);PositionWeatherCard();
    }
    void SetWeatherVisible(bool visible){state.WeatherWidget.Visible=visible;Save();UpdateWeatherCard();}
    void PositionWeatherCard()
    {
        if(weatherCard is null||!loaded||!state.WeatherWidget.Visible)return;
        var safe=NativeServices.WorkingArea(this);var area=DockLayout.Widgets(new(safe.Left,safe.Top,safe.Width,safe.Height),new(Left,Top,Width,Height)).Weather;
        var p=state.WeatherWidget.Placement;
        if(area.Height<48&&p.PixelX is null){weatherCard.Hide();return;}
        weatherCard.RestoreOrPlace(area,state.WeatherWidget.Collapsed);weatherCard.Show();
        RememberInitialPlacement(weatherCard,p);
    }
    void RememberInitialPlacement(Window card,DesktopCardPlacement placement)
    {
        if(placement.PixelX is not null||!card.IsVisible)return;
        var pixel=NativeServices.PixelPosition(card);placement.PixelX=pixel.X;placement.PixelY=pixel.Y;placement.Width=card.Width;
        if(card.Height>48)placement.Height=card.Height;Save();
    }

    async Task RefreshWeatherAsync()
    {
        if (weatherLifetime.IsCancellationRequested) return;
        weatherRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(weatherLifetime.Token);
        weatherRequest = request;
        var location = state.Location;
        try
        {
            LifecycleLog.Write("weather-refresh-start");
            var reading = await weatherService.GetAsync(location, request.Token);
            if (!request.IsCancellationRequested && loaded && state.Location == location)
            { state.Weather = reading; UpdateWeatherLabel(false);ScheduleWeatherRefresh(false);LifecycleLog.Write($"weather-updated hours={reading.Hourly.Count} days={reading.Daily.Count}"); Save(); }
        }
        catch (OperationCanceledException) { if (!request.IsCancellationRequested && loaded&&state.Location==location){UpdateWeatherLabel(true);ScheduleWeatherRefresh(true);LifecycleLog.Write("weather-timeout");} }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException)
        { if (!request.IsCancellationRequested && loaded && state.Location == location){UpdateWeatherLabel(true);ScheduleWeatherRefresh(true);LifecycleLog.Write("weather-offline "+ex.GetType().Name+" "+ex.GetBaseException().Message.Replace('\n',' ').Replace('\r',' '));} }
        finally { if (ReferenceEquals(weatherRequest, request)) weatherRequest = null; }
    }
    void ScheduleWeatherRefresh(bool failed)
    {
        weatherFailures=failed?Math.Min(5,weatherFailures+1):0;
        weatherTimer.Stop();
        weatherTimer.Interval=failed?TimeSpan.FromSeconds(weatherFailures switch{1=>30,2=>60,3=>120,_=>300}):TimeSpan.FromMinutes(20);
        if(loaded&&!weatherLifetime.IsCancellationRequested)weatherTimer.Start();
    }

    ContextMenu WeatherMenu()
    {
        var context = new ContextMenu();
        context.Items.Add(new MenuItem { Header = "天气 · " + state.Location.Name, IsEnabled = false });
        context.Items.Add(Entry("刷新天气", () => _ = RefreshWeatherAsync()));
        context.Items.Add(Entry("更换城市…", ChooseWeatherCity));
        context.Items.Add(new Separator());
        context.Items.Add(Entry("天气数据：Open-Meteo ↗", () => Try(() => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://open-meteo.com/") { UseShellExecute = true }))));
        return context;
    }

    void ChooseWeatherCity()
    {
        var dialog = new Window { Owner = this, Title = "选择天气城市", Width = 410, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush("#F6F7FA"), Topmost = true };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "输入城市名，再选择搜索结果", Margin = new Thickness(0, 0, 0, 10) });
        var input = new TextBox { Text = state.Location.Name.Split(" · ")[0], Padding = new Thickness(6), FontSize = 14, MaxLength = 100 };
        var search = new Button { Content = "搜索城市", Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 8, 0, 8), IsDefault = true };
        var places = new ComboBox { DisplayMemberPath = nameof(WeatherLocation.Name), Margin = new Thickness(0, 0, 0, 8), Height = 30 };
        var status = new TextBlock { Text = "支持中文、日文或英文城市名。", FontSize = 11, Foreground = Brushes.DimGray };
        var apply = new Button { Content = "使用此城市", IsEnabled = false, Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var item in new UIElement[] { input, search, places, status, apply }) panel.Children.Add(item);
        dialog.Content = panel;
        using var cancel = new CancellationTokenSource();
        dialog.Closed += (_, _) => cancel.Cancel();
        search.Click += async (_, _) =>
        {
            search.IsEnabled = false; apply.IsEnabled = false; status.Text = "正在搜索…";
            try
            {
                var locations = await weatherService.SearchAsync(input.Text, cancel.Token);
                if (cancel.IsCancellationRequested) return;
                places.ItemsSource = locations; places.SelectedIndex = locations.Count > 0 ? 0 : -1;
                status.Text = locations.Count > 0 ? "请核对城市及国家。" : "没有找到，请尝试英文名或加上国家。";
            }
            catch (OperationCanceledException) { if (!cancel.IsCancellationRequested) status.Text = "搜索超时，请稍后重试。"; }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException) { status.Text = "暂时无法搜索，请检查网络后重试。"; }
            finally { if (!cancel.IsCancellationRequested) search.IsEnabled = true; }
        };
        places.SelectionChanged += (_, _) => apply.IsEnabled = places.SelectedItem is WeatherLocation;
        apply.Click += (_, _) =>
        {
            if (places.SelectedItem is not WeatherLocation location) return;
            state.Location = location; state.Weather = null; UpdateWeatherLabel(false); Save();
            dialog.DialogResult = true; _ = RefreshWeatherAsync();
        };
        dialog.Loaded += (_, _) => {
            input.Focus(); input.SelectAll();
            var area=NativeServices.WorkingArea(dialog);
            var position=DockMath.KeepVisible(dialog.Left,dialog.Top,dialog.ActualWidth,dialog.ActualHeight,area.Left,area.Top,area.Width,area.Height);
            dialog.Left=position.X;dialog.Top=position.Y;
        };
        dialog.ShowDialog();
    }
}
