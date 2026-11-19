using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace FloatingDock;

public static class GlassUi
{
    public static SolidColorBrush Color(string hex)=>(SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    public static Brush Text=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:Color("#F1F4FA");
    public static Brush Muted=>SystemParameters.HighContrast?SystemColors.WindowTextBrush:Color("#D5DFED");
    public static Brush Panel=>SystemParameters.HighContrast?SystemColors.WindowBrush:Color("#E51B2738");
    public static ControlTemplate ButtonTemplate()
    {
        var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.Name="Chrome";
        border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));
        border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));
        var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;
        var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,Color("#25FFFFFF"),"Chrome"));template.Triggers.Add(hover);
        var focus=new Trigger{Property=UIElement.IsKeyboardFocusedProperty,Value=true};focus.Setters.Add(new Setter(Border.BorderBrushProperty,Color("#A09EC6FF"),"Chrome"));focus.Setters.Add(new Setter(Border.BorderThicknessProperty,new Thickness(1),"Chrome"));template.Triggers.Add(focus);
        var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.4));template.Triggers.Add(disabled);return template;
    }
    public static Style ScrollBarStyle()=>(Style)System.Windows.Markup.XamlReader.Parse("""
      <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="{x:Type ScrollBar}">
        <Setter Property="Width" Value="12"/><Setter Property="Background" Value="Transparent"/>
        <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="{x:Type ScrollBar}"><Grid Background="Transparent">
          <Track x:Name="PART_Track" Orientation="Vertical" IsDirectionReversed="True" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}">
            <Track.DecreaseRepeatButton><RepeatButton Focusable="False" Command="{x:Static ScrollBar.PageUpCommand}"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
            <Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#708EA3BE" CornerRadius="4" Margin="3,0"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            <Track.IncreaseRepeatButton><RepeatButton Focusable="False" Command="{x:Static ScrollBar.PageDownCommand}"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
          </Track>
        </Grid></ControlTemplate></Setter.Value></Setter>
      </Style>
      """);
    public static void Menu(ContextMenu menu)
    {
        if (SystemParameters.HighContrast) return;
        menu.Background=Color("#F0253245");menu.Foreground=Text;menu.BorderBrush=Color("#405F748F");menu.BorderThickness=new Thickness(1);menu.FontFamily=new FontFamily("Microsoft YaHei UI");menu.FontSize=12;
        menu.Resources[SystemColors.MenuBrushKey]=menu.Background;menu.Resources[SystemColors.MenuTextBrushKey]=Text;menu.Resources[SystemColors.HighlightBrushKey]=Color("#FF405B7D");menu.Resources[SystemColors.HighlightTextBrushKey]=Text;
        menu.Opened += (_, _) => ApplyMenuChrome(menu);
        menu.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((_, args) =>
        {
            if (args.OriginalSource is MenuItem item) ApplySubmenuChrome(menu, item);
        }));
    }

    static void ApplyMenuChrome(ContextMenu menu)
    {
        if (menu.Template.FindName("ContextMenuBorder", menu) is Border chrome)
        {
            chrome.CornerRadius = new CornerRadius(9);
            if (SystemParameters.ClientAreaAnimation)
            {
                var fade = new DoubleAnimation(.72, 1, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                };
                chrome.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }

        // The Windows template draws a light icon strip separately from MenuBrushKey.
        // Keep its checkmark column, scrolling and keyboard behavior.
        if (menu.Template.FindName("ItemsPresenter", menu) is ItemsPresenter presenter &&
            VisualTreeHelper.GetParent(presenter) is Grid chromeGrid)
        {
            foreach (var strip in chromeGrid.Children.OfType<Rectangle>())
                strip.Fill = Brushes.Transparent;
        }

        StyleMenuItems(menu, menu);
    }

    static void ApplySubmenuChrome(ContextMenu menu, MenuItem submenu)
    {
        submenu.ApplyTemplate();
        if (submenu.Template.FindName("SubMenuBorder", submenu) is Border chrome)
        {
            chrome.Background = menu.Background;
            chrome.BorderBrush = menu.BorderBrush;
            chrome.CornerRadius = new CornerRadius(9);
            foreach (var rectangle in MenuSurfaceRectangles(chrome))
            {
                if (rectangle.Name == "OpaqueRect") rectangle.Fill = menu.Background;
                else if (rectangle.Width <= 2 || (rectangle.ActualWidth > 0 && rectangle.ActualWidth <= 2)) rectangle.Fill = Brushes.Transparent;
            }
        }
        StyleMenuItems(menu, submenu);
    }

    // Keep the platform's scroll controls and the menu items' own checkmark visuals.
    static IEnumerable<Rectangle> MenuSurfaceRectangles(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is MenuItem) continue;
            if (child is Rectangle rectangle) yield return rectangle;
            foreach (var nested in MenuSurfaceRectangles(child)) yield return nested;
        }
    }

    static void StyleMenuItems(ContextMenu menu, ItemsControl owner)
    {
        foreach (var item in owner.Items.OfType<MenuItem>().Where(item => item.IsEnabled))
        {
            item.ApplyTemplate();
            if (item.IsCheckable && item.Template.FindName("Glyph", item) is Shape glyph)
            {
                glyph.Fill = menu.Foreground;
                if (item.Template.FindName("GlyphPanel", item) is Border check)
                {
                    check.Background = menu.BorderBrush;
                    check.BorderBrush = menu.BorderBrush;
                }
            }
            if (item.Template.FindName("RightArrow", item) is Shape arrow)
                arrow.Fill = menu.Foreground;
        }
    }
}
