using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FloatingDock;

public sealed class DockLinkedApps
{
    sealed record Entry(string Launch, string ProcessName);
    readonly Dictionary<string, Entry> entries;
    readonly string heartbeats;
    static readonly TimeSpan Freshness = TimeSpan.FromSeconds(15);

    DockLinkedApps(string hubRoot, Dictionary<string, Entry> entries)
    {
        this.entries = entries;
        heartbeats = Path.Combine(hubRoot, "data", "heartbeats");
    }

    public static DockLinkedApps? Load(string appDirectory)
    {
        try
        {
            var root = AppLinkPresence.ReadHubRoot(appDirectory);
            if (root is null) return null;
            using var registry = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "registry.json")));
            var links = new Dictionary<string, Entry>();
            foreach (var project in registry.RootElement.GetProperty("projects").EnumerateArray())
            {
                var id = project.GetProperty("id").GetString();
                if (id is not ("workbench" or "pet") || !project.TryGetProperty("launch", out var launch)
                    || launch.GetString() is not { } path || !Path.IsPathFullyQualified(path)
                    || !project.TryGetProperty("command", out var command)
                    || command.ValueKind != JsonValueKind.Array || command.GetArrayLength() == 0
                    || command[0].GetString() is not { } executable)
                    continue;
                links[id] = new Entry(Path.GetFullPath(path), Path.GetFileNameWithoutExtension(executable));
            }
            return links.Count == 0 ? null : new DockLinkedApps(root, links);
        }
        catch (Exception exception) when (IsReadError(exception)) { return null; }
    }

    public string? FindId(string shortcut)
    {
        foreach (var (id, entry) in entries)
            if (string.Equals(shortcut, entry.Launch, StringComparison.OrdinalIgnoreCase)) return id;
        return null;
    }

    public Dictionary<string, int> ReadRunning()
    {
        var current = new Dictionary<string, int>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in entries)
        {
            try
            {
                DateTimeOffset newest = DateTimeOffset.MinValue;
                foreach (var file in Directory.EnumerateFiles(heartbeats, id + "-*.json"))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.Length > 2048 || now - info.LastWriteTimeUtc > Freshness) continue;
                        using var body = JsonDocument.Parse(File.ReadAllText(file));
                        var record = body.RootElement;
                        if (record.GetProperty("app_id").GetString() != id ||
                            !record.GetProperty("pid").TryGetInt32(out int pid) || pid <= 0 ||
                            Path.GetFileNameWithoutExtension(file) != id + "-" + pid ||
                            !record.GetProperty("time").TryGetDouble(out double unix) || !double.IsFinite(unix))
                            continue;
                        var updated = DateTimeOffset.UnixEpoch.AddSeconds(unix);
                        if (updated <= newest || now - updated > Freshness || updated > now.AddSeconds(2)) continue;
                        if (!MatchesProcess(entry, pid, updated)) continue;

                        newest = updated;
                        current[id] = pid;
                    }
                    catch (Exception exception) when (IsReadError(exception)) { }
                }
            }
            catch (Exception exception) when (IsReadError(exception)) { }
        }
        return current;
    }

    static bool MatchesProcess(Entry entry, int pid, DateTimeOffset updated)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited || process.StartTime.ToUniversalTime() > updated.UtcDateTime) return false;
            var actual = process.ProcessName;
            return string.Equals(entry.ProcessName, actual, StringComparison.OrdinalIgnoreCase)
                || (IsPython(entry.ProcessName) && IsPython(actual));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    static bool IsPython(string name)
        => name.Equals("python", StringComparison.OrdinalIgnoreCase) || name.Equals("pythonw", StringComparison.OrdinalIgnoreCase);

    static bool IsReadError(Exception exception)
        => exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException
            or ArgumentException or InvalidOperationException or NotSupportedException;

    internal static void AllowForeground(int pid) => AllowSetForegroundWindow(pid);

    public static bool SignalPet(int pid)
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting("Local\\PersonalDragonPet.Reveal." + pid);
            return signal.Set();
        }
        catch (Exception exception) when (exception is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(int process);
}

