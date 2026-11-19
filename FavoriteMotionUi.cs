using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FloatingDock;

public sealed partial class DockWindow
{
    readonly DispatcherTimer favoritePreviewLeave = new() { Interval = TimeSpan.FromMilliseconds(70) };
    int favoriteCount;
    string? favoriteDragId;
    Point favoriteDragPoint;
    int favoriteSource = -1;
    int favoriteDestination = -1;
    double ScrollingOrigin => Math.Max(0, (ScrollingWidth - ScrollingCount * DockMath.Slot) / 2) - scroll;

    void InitializeFavoritePreview()
    {
        favoritePreviewLeave.Tick += (_, _) => ClearFavoritePreview();
        strip.DragLeave += (_, _) =>
        {
            if (favoriteDragId is null) dropMarker.Visibility = Visibility.Collapsed;
            else favoritePreviewLeave.Start();
        };
        Closed += (_, _) => favoritePreviewLeave.Stop();
    }

    static void PrepareFavoriteFace(Tile tile)
    {
        tile.FaceScale = new ScaleTransform(1, 1);
        tile.FaceLift = new TranslateTransform();
        var transform = new TransformGroup();
        transform.Children.Add(tile.FaceScale);
        transform.Children.Add(tile.FaceLift);
        tile.Button.Content = new Grid
        {
            RenderTransform = transform, RenderTransformOrigin = new Point(.5, .5), IsHitTestVisible = false
        };
    }

    static void SetFavoriteFace(Tile tile, bool hover, bool down, double blend)
    {
        double factor = SystemParameters.ClientAreaAnimation ? blend : 1;
        double scale = down ? .92 : hover ? 1.06 : 1;
        double lift = down ? 1 : hover ? -4 : 0;
        var size = tile.FaceScale!;
        size.ScaleX = size.ScaleY = MoveToward(size.ScaleX, scale, factor);
        var position = tile.FaceLift!;
        position.Y = MoveToward(position.Y, lift, factor);
    }

    // Slot centers never move with a hover effect or the previewed icons.
    double FavoriteSlotCenter(int index) => index < fixedCount
        ? (index + .5) * DockMath.Slot
        : FixedSpan + ScrollingOrigin + (index - fixedCount + .5) * DockMath.Slot;

    bool OverFavoriteLane(double x, bool fixedLane)
    {
        if (fixedLane) return fixedCount > 0 && x >= 0 && x < FixedSpan;
        if (favoriteCount <= fixedCount) return false;
        double left = Math.Max(FixedSpan, FavoriteSlotCenter(fixedCount) - DockMath.Slot / 2);
        double right = Math.Min(Viewport, FavoriteSlotCenter(favoriteCount - 1) + DockMath.Slot / 2);
        return left <= x && x <= right;
    }

    int FavoriteInsertionIndex(int from, Point point)
    {
        if (from < 0 || point.Y is < 60 or > 176 || point.X < 0 || point.X > Viewport) return -1;
        bool fixedLane = point.X < FixedSpan;
        if (!OverFavoriteLane(point.X, fixedLane)) return -1;

        int start = fixedLane ? 0 : fixedCount;
        int end = fixedLane ? fixedCount : favoriteCount;
        int result = start - (from < start ? 1 : 0);
        for (int index = start; index < end; index++)
        {
            if (index == from) continue;
            if (point.X < FavoriteSlotCenter(index)) break;
            result++;
        }

        double center = FavoriteSlotCenter(result);
        return result >= fixedCount && (center < FixedSpan || center > Viewport) ? -1 : result;
    }

    void TrackFavoritePreview(string id, Point point)
    {
        favoritePreviewLeave.Stop();
        favoriteDragId = id;
        favoriteDragPoint = point;
        favoriteSource = tiles.FindIndex(tile => tile.Favorite && tile.Item.Id == id);
        if (favoriteCount > fixedCount && point.Y is >= 60 and <= 176 && point.X >= FixedSpan && point.X <= Viewport)
        {
            if (point.X < FixedSpan + 22) ScrollBy(-9);
            else if (point.X > Viewport - 22 && OverFavoriteLane(point.X, fixedLane: false))
            {
                var lastFavoriteScroll = Math.Max(0, (favoriteCount - fixedCount) * DockMath.Slot - ScrollingWidth);
                if (targetScroll < lastFavoriteScroll)
                    targetScroll = Math.Min(targetScroll + 9, lastFavoriteScroll);
            }
        }
        UpdateFavoriteInsertion();
    }

    void UpdateFavoriteInsertion()
    {
        if (favoriteDragId is null) return;
        favoriteDestination = FavoriteInsertionIndex(favoriteSource, favoriteDragPoint);
        dropMarker.Visibility = favoriteDestination >= 0 ? Visibility.Visible : Visibility.Collapsed;
        if (favoriteDestination < 0) return;

        double center = FavoriteSlotCenter(favoriteDestination);
        Set(dropMarker, Math.Clamp(center - dropMarker.Width / 2, 0, Math.Max(0, Viewport - dropMarker.Width)), 97);
    }

    void ClearFavoritePreview()
    {
        favoritePreviewLeave.Stop();
        favoriteDragId = null;
        favoriteSource = favoriteDestination = -1;
        dropMarker.Visibility = Visibility.Collapsed;
    }

    (double Center, bool Fixed) PositionFavorite(Tile tile, int index, double blend)
    {
        int slot = index;
        if (favoriteDestination >= 0 && index != favoriteSource)
        {
            if (favoriteSource < index && index <= favoriteDestination) slot--;
            else if (favoriteDestination <= index && index < favoriteSource) slot++;
        }

        double offset = FavoriteSlotCenter(slot) - tile.Center;
        tile.PreviewOffset = MoveToward(tile.PreviewOffset, offset, SystemParameters.ClientAreaAnimation ? blend : 1);
        double center = tile.Center + tile.PreviewOffset;
        // Keep a sliding icon visible until it actually crosses the fixed-area divider.
        bool fixedLane = slot < fixedCount ||
            (tile.Button.Parent == fixedTiles && center < FixedSpan && Math.Abs(tile.PreviewOffset - offset) > .2);
        var lane = fixedLane ? fixedTiles : scrollingTiles;
        PlaceInLane(tile.Reflection, lane);
        PlaceInLane(tile.Button, lane);
        if (tile.Lamp is not null) PlaceInLane(tile.Lamp, lane);
        return (center, fixedLane);
    }

    bool CommitFavoriteDrop(string id, Point point)
    {
        int from = tiles.FindIndex(tile => tile.Favorite && tile.Item.Id == id);
        int to = FavoriteInsertionIndex(from, point);
        if (to < 0 || from == to)
        {
            ClearFavoritePreview();
            return false;
        }

        var currentPositions = new Dictionary<Tile, double>();
        for (int index = Math.Min(from, to); index <= Math.Max(from, to); index++)
            currentPositions[tiles[index]] = FavoriteSlotCenter(index) + tiles[index].PreviewOffset;

        var tile = tiles[from];
        var targetId = tiles[to].Item.Id;
        int savedFrom = state.FavoriteIds.IndexOf(id), savedTo = state.FavoriteIds.IndexOf(targetId);
        if (savedFrom < 0 || savedTo < 0)
        {
            ClearFavoritePreview();
            return false;
        }
        state.FavoriteIds.RemoveAt(savedFrom);
        state.FavoriteIds.Insert(savedTo, id);
        tiles.RemoveAt(from);
        tiles.Insert(to, tile);
        ClearFavoritePreview();

        for (int index = Math.Min(from, to); index <= Math.Max(from, to); index++)
        {
            var neighbor = tiles[index];
            neighbor.PreviewOffset = neighbor == tile ? 0 : currentPositions[neighbor] - FavoriteSlotCenter(index);
        }
        Save();
        return true;
    }

    static double MoveToward(double value, double target, double blend)
        => Math.Abs(value - target) < .025 ? target : value + (target - value) * blend;
}

