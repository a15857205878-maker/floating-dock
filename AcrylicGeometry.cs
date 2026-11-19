namespace FloatingDock;

public enum WallpaperPlacement { Center, Tile, Stretch, Fit, Fill, Span }
public readonly record struct MaterialRect(double X,double Y,double Width,double Height);
public static class AcrylicGeometry
{
    public static MaterialRect WallpaperBounds(double imageWidth,double imageHeight,MaterialRect monitor,WallpaperPlacement placement,MaterialRect virtualScreen)
    {
        if(imageWidth<=0||imageHeight<=0||!double.IsFinite(imageWidth)||!double.IsFinite(imageHeight))return monitor;
        var area=placement==WallpaperPlacement.Span?virtualScreen:monitor;
        if(placement==WallpaperPlacement.Stretch)return area;
        if(placement==WallpaperPlacement.Tile)return new(area.X,area.Y,imageWidth,imageHeight);
        double scale=placement switch {WallpaperPlacement.Center=>1,WallpaperPlacement.Fit=>Math.Min(area.Width/imageWidth,area.Height/imageHeight),_=>Math.Max(area.Width/imageWidth,area.Height/imageHeight)};
        double width=imageWidth*scale,height=imageHeight*scale;
        return new(area.X+(area.Width-width)/2,area.Y+(area.Height-height)/2,width,height);
    }
}
