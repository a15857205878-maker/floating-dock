namespace FloatingDock;
public sealed record DockArea(double Left,double Top,double Width,double Height){public double Right=>Left+Width;public double Bottom=>Top+Height;}
public sealed record IslandLayout(DockArea Info,DockArea Apps,DockArea Tools,double AppLeft,double AppRight,double GroupWidth);
[Flags]public enum CardEdge { None=0,Left=1,Top=2,Right=4,Bottom=8 }
public static class DockLayout
{
 public static DockArea MusicCard(DockArea safe,DockArea dock,IReadOnlyList<DockArea> occupied)
 {
  var reference=occupied.FirstOrDefault()??Widgets(safe,dock).Weather;
  double width=Math.Min(304,safe.Width);
  foreach(double height in new[]{216d,48}){
   var candidates=new[]{new DockArea(reference.Left-width-16,reference.Top,width,height),new(reference.Left,reference.Bottom+16,width,height),new(reference.Left,reference.Top-height-16,width,height),new(safe.Left,safe.Top+12,width,height),new(safe.Right-width,safe.Top+12,width,height),new(safe.Left,dock.Top-height-16,width,height)};
   foreach(var candidate in candidates){
    if(candidate.Left<safe.Left||candidate.Top<safe.Top||candidate.Right>safe.Right||candidate.Bottom>safe.Bottom)continue;
    if(occupied.Append(dock).All(r=>candidate.Right<=r.Left||candidate.Left>=r.Right||candidate.Bottom<=r.Top||candidate.Top>=r.Bottom))return candidate;
   }
  }
  return new(safe.Left,safe.Top,width,0);
 }
 public static (double X,double Y) FloatingPosition(DockArea area,double width,double height,bool top)=>DockMath.KeepVisible(area.Left+(area.Width-width)/2,top?area.Top+4:area.Bottom-height-28,width,height,area.Left,area.Top,area.Width,area.Height);
 public static IslandLayout LauncherWidth(double width)
 {
  double tools=width>=700?284:width>=300?84:40,group=width>=420?56:28;
  return new(new(8,84,22,92),new(32,84,Math.Max(1,width-tools-48),92),new(width-tools-8,84,tools,92),38+group+14,width-tools-22,group);
 }
 public static (DockArea Weather,DockArea Downloads,DockArea Folders) Widgets(DockArea safe,DockArea dock)
 {
  double top=safe.Top+12,bottom=safe.Bottom-12;
  if(dock.Top+ dock.Height/2<safe.Top+safe.Height/2)top=Math.Max(top,dock.Bottom+12);else bottom=Math.Min(bottom,dock.Top-16);
  double available=Math.Max(0,bottom-top);
  if(available<168){var none=new DockArea(safe.Left,safe.Top,Math.Min(304,safe.Width),0);return(none,none,none);}
  if(safe.Width<560||available<388){double width=Math.Min(380,safe.Width),x=safe.Left+(safe.Width-width)/2;return(new(x,top,width,48),new(x,top+60,width,48),new(x,top+120,width,48));}
  double weatherWidth=Math.Min(380,(safe.Width-16)*.55),fileWidth=Math.Min(304,safe.Width-weatherWidth-16),height=Math.Min(392,available);
  double left=safe.Left+(safe.Width-weatherWidth-fileWidth-16)/2,y=top+Math.Max(0,(available-height)*.38),right=left+weatherWidth+16;
  return(new(left,y,weatherWidth,height),new(right,y,fileWidth,200),new(right,y+212,fileWidth,height-212));
 }
 public static DockArea FitCard(DockArea card,DockArea safe,bool collapsed)
 {
  double width=Math.Clamp(double.IsFinite(card.Width)?card.Width:304,Math.Min(240,safe.Width),safe.Width);
  double height=collapsed?Math.Min(48,safe.Height):Math.Clamp(double.IsFinite(card.Height)?card.Height:286,Math.Min(180,safe.Height),safe.Height);
  double x=Math.Clamp(double.IsFinite(card.Left)?card.Left:safe.Left,safe.Left,safe.Right-width);
  double y=Math.Clamp(double.IsFinite(card.Top)?card.Top:safe.Top,safe.Top,safe.Bottom-height);
  return new(x,y,width,height);
 }
 public static DockArea AdjustCard(DockArea card,DockArea safe,CardEdge edge,double dx,double dy,bool collapsed)
 {
  card=FitCard(card,safe,collapsed);dx=double.IsFinite(dx)?dx:0;dy=double.IsFinite(dy)?dy:0;
  if(edge==CardEdge.None)return FitCard(card with{Left=card.Left+dx,Top=card.Top+dy},safe,collapsed);
  double left=card.Left,right=card.Right,top=card.Top,bottom=card.Bottom,minWidth=Math.Min(240,safe.Width),minHeight=Math.Min(180,safe.Height);
  if(edge.HasFlag(CardEdge.Left))left=Math.Clamp(left+dx,safe.Left,right-minWidth);
  if(edge.HasFlag(CardEdge.Right))right=Math.Clamp(right+dx,left+minWidth,safe.Right);
  if(!collapsed){if(edge.HasFlag(CardEdge.Top))top=Math.Clamp(top+dy,safe.Top,bottom-minHeight);if(edge.HasFlag(CardEdge.Bottom))bottom=Math.Clamp(bottom+dy,top+minHeight,safe.Bottom);}
  return new(left,top,right-left,bottom-top);
 }
 public static IslandLayout ForWidth(double width)
 {
  double gap=width<420?8:width<650?12:20,info=width<316?12:width<420?68:width<600?88:width<850?140:174,tools=width<316?100:width<420?120:width<600?144:width<850?176:296,group=width<316?0:width<420?28:width<600?42:64;
  var left=new DockArea(8,88,info,68);var right=new DockArea(width-8-tools,88,tools,68);
  var middle=new DockArea(left.Right+gap,88,right.Left-gap-left.Right-gap,68);
  return new(left,middle,right,middle.Left+(group==0?4:group+12),middle.Right-(group==0?4:8),group);
 }
 public static DockArea SafeArea(DockArea monitor,DockArea work,double leftTaskbarWidth)
 {
  double left=Math.Max(work.Left,monitor.Left+Math.Max(0,leftTaskbarWidth))+12;
  double top=work.Top+8,right=work.Right-12,bottom=work.Bottom-8;
  return new(left,top,Math.Max(1,right-left),Math.Max(1,bottom-top));
 }
 public static DockArea Panel(DockArea area,double anchorX,double anchorY,double anchorWidth,double anchorHeight,double width,double height)
 {
  width=Math.Min(width,area.Width);height=Math.Min(height,area.Height);
  double x=Math.Clamp(anchorX+anchorWidth-width,area.Left,Math.Max(area.Left,area.Right-width));
  double desired=anchorY+anchorHeight/2<area.Top+area.Height/2?anchorY+anchorHeight+12:anchorY-height-12;
  double y=Math.Clamp(desired,area.Top,Math.Max(area.Top,area.Bottom-height));return new(x,y,width,height);
 }
 public static (DockArea Downloads,DockArea Folders) Cards(DockArea safe,DockArea dock,bool downloadsCollapsed,bool foldersCollapsed)
 {
  double top=safe.Top+12,bottom=safe.Bottom-12;
  if(dock.Top+ dock.Height/2<safe.Top+safe.Height/2)top=Math.Max(top,dock.Bottom+12);
  else bottom=Math.Min(bottom,dock.Top-12);
  double available=Math.Max(0,bottom-top),gap=12;
  if(available<48)return(new(safe.Left,safe.Top,1,0),new(safe.Left,safe.Top,1,0));
  double a=downloadsCollapsed?48:286,b=foldersCollapsed?48:250;
  if(a+b+gap>available){a=48;b=48;}
  double width=Math.Min(304,safe.Width),x=safe.Right-width;
  // On extremely short screens show title bars side by side; never cover the Dock.
  if(available<108){width=Math.Min(304,Math.Max(1,(safe.Width-gap)/2));return(new(safe.Right-width*2-gap,Math.Min(top,safe.Bottom-48),width,48),new(safe.Right-width,Math.Min(top,safe.Bottom-48),width,48));}
  return(new(x,top,width,a),new(x,top+a+gap,width,b));
 }
}

