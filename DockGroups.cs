namespace FloatingDock;
public static class DockGroups
{
 public static string Name(string id)=>id switch{"development"=>"开发AI","creative"=>"创作","work"=>"办公","system"=>"系统工具","social"=>"交流陪伴","leisure"=>"休闲",_=>"全部"};
 public static IReadOnlyList<DockItem> Visible(DockState state)=>state.Items.Where(i=>state.SelectedGroup=="all"||i.Group==state.SelectedGroup).ToArray();
 public static IEnumerable<DockItem> LauncherItems(DockState state)
 {
  var itemsById=state.Items.ToLookup(item=>item.Id,StringComparer.Ordinal);
  var pinned=state.FavoriteIds.Select(id=>itemsById[id].FirstOrDefault()).OfType<DockItem>().ToArray();
  return pinned.Concat(Visible(state).Except(pinned));
 }
}
