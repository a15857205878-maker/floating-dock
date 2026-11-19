using System.IO;

namespace FloatingDock;

public sealed record CollectionEntry(string Name, string Path, bool IsDirectory);

public static class FileCollection
{
    public static IReadOnlyList<CollectionEntry> Read(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return [];
        var result = new List<CollectionEntry>();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                try
                {
                    var attributes = File.GetAttributes(path);
                    if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    result.Add(new(Path.GetFileName(path), path, (attributes & FileAttributes.Directory) != 0));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return result.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
