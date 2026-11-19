using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace FloatingDock;
public sealed partial class DesktopMusicCard
{
    DesktopCardPlacement placement=new();
    Action? savePlacement;
    bool manualRestored,geometryBusy,geometryCollapsed;
    readonly List<Thumb> resizeHandles=[];
    public void ConfigurePlacement(DesktopCardPlacement settings,Action save){placement=settings;savePlacement=save;}
    FrameworkElement MovableTitle(string title)
    {
        var label=new Grid{Background=Brushes.Transparent};
        label.Children.Add(new TextBlock{Text=title,Foreground=GlassUi.Text,FontSize=13,VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false,TextTrimming=TextTrimming.CharacterEllipsis});
        label.Children.Add(GeometryHandle("CardMove",CardEdge.None,Cursors.SizeAll));return label;
    }
    FrameworkElement ResizableCard(Border card)
    {
        var surface=new Grid();surface.Children.Add(card);
        foreach(var edge in new[]{CardEdge.Left,CardEdge.Right,CardEdge.Top,CardEdge.Bottom,CardEdge.Left|CardEdge.Top,CardEdge.Right|CardEdge.Top,CardEdge.Left|CardEdge.Bottom,CardEdge.Right|CardEdge.Bottom})
        {
            bool horizontal=edge.HasFlag(CardEdge.Left)||edge.HasFlag(CardEdge.Right),vertical=edge.HasFlag(CardEdge.Top)||edge.HasFlag(CardEdge.Bottom);
            var cursor=horizontal&&vertical?((edge== (CardEdge.Left|CardEdge.Top)||edge==(CardEdge.Right|CardEdge.Bottom))?Cursors.SizeNWSE:Cursors.SizeNESW):horizontal?Cursors.SizeWE:Cursors.SizeNS;
            var handle=GeometryHandle("CardResize"+edge.ToString().Replace(", ",""),edge,cursor);
            if(horizontal){handle.Width=6;handle.HorizontalAlignment=edge.HasFlag(CardEdge.Left)?HorizontalAlignment.Left:HorizontalAlignment.Right;}
            if(vertical){handle.Height=6;handle.VerticalAlignment=edge.HasFlag(CardEdge.Top)?VerticalAlignment.Top:VerticalAlignment.Bottom;}
            if(horizontal&&vertical){handle.Width=handle.Height=12;}else handle.Margin=horizontal?new Thickness(0,12,0,12):new Thickness(12,0,12,0);
            surface.Children.Add(handle);resizeHandles.Add(handle);
        }
        Loaded+=(_,_)=>{SystemEvents.DisplaySettingsChanged+=CardDisplayChanged;SystemEvents.UserPreferenceChanged+=CardWorkAreaChanged;};
        DpiChanged+=(_,_)=>Dispatcher.BeginInvoke(ConstrainCurrentCard);
        Closed+=(_,_)=>{SystemEvents.DisplaySettingsChanged-=CardDisplayChanged;SystemEvents.UserPreferenceChanged-=CardWorkAreaChanged;};
        return surface;
    }
    Thumb GeometryHandle(string name,CardEdge edge,Cursor cursor)
    {
        var handle=new Thumb{Name=name,Cursor=cursor,Background=Brushes.Transparent,Focusable=false,ToolTip=edge==CardEdge.None?"拖动移动卡片":"拖动调整大小"};
        var template=new ControlTemplate(typeof(Thumb));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.BackgroundProperty,Brushes.Transparent);template.VisualTree=border;handle.Template=template;
        AutomationProperties.SetName(handle,edge==CardEdge.None?"拖动"+Title:"调整"+Title+"大小");
        handle.DragStarted+=(_,_)=>geometryBusy=true;
        handle.DragDelta+=(_,e)=>{
            var safe=NativeServices.WorkingArea(this);var current=new DockArea(Left,Top,Width,Height);
            var next=DockLayout.AdjustCard(current,new(safe.Left,safe.Top,safe.Width,safe.Height),edge,e.HorizontalChange,e.VerticalChange,geometryCollapsed);
            Place(next,geometryCollapsed);e.Handled=true;
        };
        handle.DragCompleted+=(_,_)=>{
            geometryBusy=false;var pixels=NativeServices.PixelPosition(this);placement.PixelX=pixels.X;placement.PixelY=pixels.Y;placement.Width=Width;
            if(!geometryCollapsed)placement.Height=Height;
            manualRestored=true;savePlacement?.Invoke();RefreshLayout();
        };return handle;
    }
    public void RestoreOrPlace(DockArea fallback,bool collapsed)
    {
        if(placement.PixelX is null&&fallback.Height<=48)placement.AutoCollapsed=true;
        collapsed|=placement.AutoCollapsed;
        geometryCollapsed=collapsed;
        if(placement.PixelX is null||placement.PixelY is null)
        {
            Place(collapsed?fallback with{Height=Math.Min(48,fallback.Height)}:fallback,collapsed);geometryCollapsed=collapsed||fallback.Height<=48;
        }
        else
        {
            if(!manualRestored)
            {
                Place(new(fallback.Left,fallback.Top,placement.Width,collapsed?48:placement.Height),collapsed);
                if(!IsVisible)Show();
                NativeServices.RestorePixelPosition(this,placement.PixelX.Value,placement.PixelY.Value);manualRestored=true;
            }
            Place(new(Left,Top,placement.Width,collapsed?48:placement.Height),collapsed);ConstrainCurrentCard();
        }
        foreach(var handle in resizeHandles)handle.Visibility=geometryCollapsed&&handle.Name.Contains("Top")||geometryCollapsed&&handle.Name.Contains("Bottom")?Visibility.Collapsed:Visibility.Visible;
    }
    void CardDisplayChanged(object? sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(ConstrainCurrentCard);}
    void CardWorkAreaChanged(object sender,UserPreferenceChangedEventArgs e)=>CardDisplayChanged(sender,e);
    void ConstrainCurrentCard()
    {
        if(closed||!IsLoaded||geometryBusy)return;
        var safe=NativeServices.WorkingArea(this);var fit=DockLayout.FitCard(new(Left,Top,Width,Height),new(safe.Left,safe.Top,safe.Width,safe.Height),geometryCollapsed);
        Place(fit,geometryCollapsed);
    }
}
