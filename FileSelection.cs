namespace FloatingDock;
public sealed class FileSelection
{
    readonly HashSet<string> selected=new(StringComparer.OrdinalIgnoreCase);
    string? anchor;
    public int Count=>selected.Count;
    public bool Contains(string path)=>selected.Contains(path);
    public void Select(string path,IReadOnlyList<string> visible,bool control,bool shift,bool preserveExisting=false)
    {
        if(shift&&anchor is not null)
        {
            int from=Array.FindIndex(visible.ToArray(),p=>p.Equals(anchor,StringComparison.OrdinalIgnoreCase));
            int to=Array.FindIndex(visible.ToArray(),p=>p.Equals(path,StringComparison.OrdinalIgnoreCase));
            if(from>=0&&to>=0){if(!control)selected.Clear();foreach(var item in visible.Skip(Math.Min(from,to)).Take(Math.Abs(to-from)+1))selected.Add(item);return;}
        }
        if(preserveExisting&&selected.Contains(path))return;
        if(!control)selected.Clear();
        if(control&&!selected.Add(path))selected.Remove(path);else selected.Add(path);
        anchor=path;
    }
    public void SelectAll(IEnumerable<string> visible){selected.Clear();selected.UnionWith(visible);}
    public void Retain(IEnumerable<string> visible){selected.IntersectWith(visible);if(anchor is not null&&!selected.Contains(anchor))anchor=null;}
}
