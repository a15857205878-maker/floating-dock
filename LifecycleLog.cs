using System.IO;

namespace FloatingDock;

internal static class LifecycleLog
{
    public static void Write(string message)
    {
        try
        {
            var directory=Path.Combine(AppContext.BaseDirectory,"data");Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"lifecycle.log"),$"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
        }
        catch(IOException){ }
        catch(UnauthorizedAccessException){ }
    }
}
