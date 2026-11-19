using System.Diagnostics;
using System.IO;

namespace FloatingDock;

public sealed record BrowserEntry(string Name, string Path, bool IsDirectory, DateTime ModifiedUtc, long Size, string Kind="", DateTime CreatedUtc=default);
public enum DesktopFileCategory { All, Folders, Documents, Images, Media, Archives, Shortcuts, Other }
public sealed record FileQuery(IReadOnlyList<string> Roots, string Search="", bool Recursive=false, bool FilesOnly=false, bool NameSort=false, int MaxScanned=10000, int MaxResults=200, int MaxDepth=6, DateTime? CreatedSinceUtc=null, DesktopFileCategory Category=DesktopFileCategory.All);
public sealed record FileQueryResult(IReadOnlyList<BrowserEntry> Entries, bool Incomplete, int Scanned, int ReadableRoots);
public static class FileBrowser
{
    public static FileQueryResult Query(FileQuery query, CancellationToken token=default)
    {
        token.ThrowIfCancellationRequested();
        var clock=Stopwatch.StartNew();
        var entries=new Dictionary<string,BrowserEntry>(StringComparer.OrdinalIgnoreCase);
        var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending=new Queue<(string Path,int Depth)>();
        bool incomplete=false;int scanned=0,readable=0;
        foreach(var root in query.Roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var path=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if(!Directory.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0){incomplete=true;continue;}
                readable++;pending.Enqueue((path,0));
            }
            catch(Exception ex) when(IsFileError(ex)){incomplete=true;}
        }
        var search=query.Search.Trim();
        while(pending.Count>0)
        {
            token.ThrowIfCancellationRequested();
            if(clock.Elapsed>TimeSpan.FromSeconds(5)){incomplete=true;break;}
            var folder=pending.Dequeue();if(!visited.Add(folder.Path))continue;
            try
            {
                foreach(var path in Directory.EnumerateFileSystemEntries(folder.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if(scanned>=Math.Max(0,query.MaxScanned)||clock.Elapsed>TimeSpan.FromSeconds(5)){incomplete=true;pending.Clear();goto Finished;}
                    scanned++;
                    try
                    {
                        var attr=File.GetAttributes(path);
                        if((attr&(FileAttributes.Hidden|FileAttributes.System|FileAttributes.ReparsePoint))!=0)continue;
                        bool directory=(attr&FileAttributes.Directory)!=0;
                        var name=Path.GetFileName(path);
                        if(!directory && IsIncompleteDownload(name))continue;
                        if(directory&&query.Recursive)
                        {
                            if(folder.Depth<query.MaxDepth)pending.Enqueue((path,folder.Depth+1));
                            else incomplete=true;
                        }
                        if(query.FilesOnly&&(directory||IsShortcut(name)))continue;
                        if(query.Category!=DesktopFileCategory.All&&Classify(name,directory)!=query.Category)continue;
                        if(search.Length>0&&!name.Contains(search,StringComparison.CurrentCultureIgnoreCase))continue;
                        FileSystemInfo info=directory?new DirectoryInfo(path):new FileInfo(path);
                        if(query.CreatedSinceUtc is DateTime since&&info.CreationTimeUtc<since)continue;
                        entries[path]=new(name,path,directory,info.LastWriteTimeUtc,directory?0:((FileInfo)info).Length,"",info.CreationTimeUtc);
                    }
                    catch(Exception ex) when(IsFileError(ex)){incomplete=true;}
                }
            }
            catch(Exception ex) when(IsFileError(ex)){incomplete=true;}
        }
        Finished:
        token.ThrowIfCancellationRequested();
        IEnumerable<BrowserEntry> ordered=query.NameSort
            ?entries.Values.OrderByDescending(e=>e.IsDirectory).ThenBy(e=>e.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(e=>e.Path,StringComparer.OrdinalIgnoreCase)
            :entries.Values.OrderByDescending(e=>e.ModifiedUtc).ThenBy(e=>e.Path,StringComparer.OrdinalIgnoreCase);
        int limit=Math.Max(1,query.MaxResults);
        return new(ordered.Take(limit).ToArray(),incomplete||entries.Count>limit,scanned,readable);
    }

    static DesktopFileCategory Classify(string name, bool directory)
    {
        if (directory) return DesktopFileCategory.Folders;
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".md" or ".rtf" or ".csv" or ".odt" or ".ods" or ".odp" => DesktopFileCategory.Documents,
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".svg" or ".bmp" or ".heic" or ".tiff" or ".ico" => DesktopFileCategory.Images,
            ".mp3" or ".m4a" or ".wav" or ".flac" or ".ogg" or ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" => DesktopFileCategory.Media,
            ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".xz" or ".bz2" => DesktopFileCategory.Archives,
            ".lnk" or ".url" or ".appref-ms" => DesktopFileCategory.Shortcuts,
            _ => DesktopFileCategory.Other
        };
    }

    static bool IsIncompleteDownload(string name)=>name.StartsWith("~$",StringComparison.Ordinal)||new[]{".crdownload",".part",".partial",".tmp"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase);
    static bool IsShortcut(string name)=>new[]{".lnk",".url",".appref-ms"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase);
    static bool IsFileError(Exception ex)=>ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;
}
