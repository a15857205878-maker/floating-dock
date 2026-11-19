using System.Windows;
using System.Windows.Media;

namespace FloatingDock;

// Resolution-independent artwork stays crisp at every desktop scaling setting.
public static class WeatherArtwork
{
    public static DrawingImage For(int? code, bool day)
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 32, 32));
            if (code is 0 or 1)
            {
                if (day) Sun(dc, new Point(16, 16), 7);
                else Moon(dc);
            }
            else
            {
                if (code == 2)
                {
                    if(day)Sun(dc,new Point(11,11),5);else Moon(dc);
                }
                var cloud = Geometry.Parse("M 7,23 C 0,23 0,14 6,13 C 5,5 17,3 21,11 C 29,9 34,22 26,23 Z");
                dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(252,254,255), Color.FromRgb(180,205,226),90), new Pen(new SolidColorBrush(Color.FromArgb(90,255,255,255)),.6), cloud);
                if(code is 45 or 48)
                {
                    var pen=new Pen(new SolidColorBrush(Color.FromRgb(174,208,226)),1.8){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
                    dc.DrawLine(pen,new Point(5,26),new Point(27,26));dc.DrawLine(pen,new Point(8,30),new Point(24,30));
                }
                else if(code is >=71 and <=77 or 85 or 86)
                {
                    var pen=new Pen(new SolidColorBrush(Color.FromRgb(210,244,255)),1.3){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
                    foreach(double x in new[]{9d,23d}){dc.DrawLine(pen,new Point(x,25),new Point(x,31));dc.DrawLine(pen,new Point(x-2.5,26.5),new Point(x+2.5,29.5));dc.DrawLine(pen,new Point(x-2.5,29.5),new Point(x+2.5,26.5));}
                }
                else if(code is >=95 and <=99)
                    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(255,207,68)),null,Geometry.Parse("M 17,20 L 11,27 L 16,27 L 14,32 L 23,24 L 18,24 L 22,20 Z"));
                else if(code is >=51 and <=67 or >=80 and <=82)
                {
                    var pen=new Pen(new SolidColorBrush(Color.FromRgb(91,190,245)),2.2){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
                    foreach(double x in new[]{9d,17d,25d})dc.DrawLine(pen,new Point(x,25),new Point(x-2,30));
                }
            }
        }
        drawing.Freeze();var result=new DrawingImage(drawing);result.Freeze();return result;
    }

    static void Moon(DrawingContext dc)
    {
        var crescent=new CombinedGeometry(GeometryCombineMode.Exclude,new EllipseGeometry(new Rect(4,3,24,25)),new EllipseGeometry(new Rect(13,-1,24,24)));
        dc.DrawGeometry(new LinearGradientBrush(Color.FromRgb(255,233,145),Color.FromRgb(255,189,55),90),null,crescent);
    }

    static void Sun(DrawingContext dc,Point center,double radius)
    {
        var pen=new Pen(new SolidColorBrush(Color.FromRgb(255,203,72)),1.6){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
        for(int i=0;i<8;i++){double a=i*Math.PI/4;dc.DrawLine(pen,new Point(center.X+Math.Cos(a)*(radius+2),center.Y+Math.Sin(a)*(radius+2)),new Point(center.X+Math.Cos(a)*(radius+4.5),center.Y+Math.Sin(a)*(radius+4.5)));}
        dc.DrawEllipse(new LinearGradientBrush(Color.FromRgb(255,232,137),Color.FromRgb(255,185,43),90),null,center,radius,radius);
    }
}
