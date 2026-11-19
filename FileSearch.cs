using System.IO;

namespace FloatingDock;
public static class FileSearch
{
 public static FileQueryResult Apply(IEnumerable<DockItem> apps,FileQueryResult files,string search,DateTime? createdSince=null)
 {
  var merged=Merge(apps,files.Entries,search,createdSince);
  return files with {Entries=merged.Take(200).ToArray(),Incomplete=files.Incomplete||merged.Count>200};
 }
 public static IReadOnlyList<BrowserEntry> Merge(IEnumerable<DockItem> apps,IEnumerable<BrowserEntry> files,string search,DateTime? createdSince=null)
 {
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<BrowserEntry>();
  foreach(var app in apps.Where(i=>i.Name.Contains(search.Trim(),StringComparison.CurrentCultureIgnoreCase)||System.IO.Path.GetFileName(i.Path).Contains(search.Trim(),StringComparison.CurrentCultureIgnoreCase)))
  {
   DateTime created=DateTime.MinValue,modified=DateTime.MinValue;
   try{FileSystemInfo info=Directory.Exists(app.Path)?new DirectoryInfo(app.Path):new FileInfo(app.Path);if(info.Exists){created=info.CreationTimeUtc;modified=info.LastWriteTimeUtc;}}
   catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){}
   if(createdSince is DateTime since&&created<since)continue;
   if(seen.Add(app.Path))result.Add(new(app.Name,app.Path,false,modified,0,"应用",created));
  }
  foreach(var file in files)if((createdSince is null||file.CreatedUtc>=createdSince.Value)&&seen.Add(file.Path))result.Add(file);
  return result;
 }
}
