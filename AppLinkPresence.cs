using System.IO;
using System.Text.Json;

namespace FloatingDock;

/// <summary>Optional local presence signal. Never blocks the dock or exposes shortcuts.</summary>
public sealed class AppLinkPresence : IDisposable
{
    private readonly System.Threading.Timer timer;
    private readonly string? heartbeat;
    private readonly object gate = new();
    private bool disposed;

    public AppLinkPresence()
    {
        try
        {
            var root = ReadHubRoot(AppContext.BaseDirectory);
            if (root is not null)
            {
                var folder = Path.Combine(root, "data", "heartbeats");
                Directory.CreateDirectory(folder);
                heartbeat = Path.Combine(folder, "dock-" + Environment.ProcessId + ".json");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or ArgumentException or InvalidOperationException) { }
        timer = new System.Threading.Timer(_ => Write(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    internal static string? ReadHubRoot(string appDirectory)
    {
        var config = Path.Combine(appDirectory, "app_link.json");
        using var document = JsonDocument.Parse(File.ReadAllText(config));
        var root = document.RootElement.GetProperty("hub_root").GetString();
        return !string.IsNullOrWhiteSpace(root) && Path.IsPathFullyQualified(root)
            && File.Exists(Path.Combine(root, "launcher.pyw")) ? root : null;
    }

    private void Write()
    {
        lock (gate)
        {
            if (disposed || heartbeat is null) return;
            try
            {
                var body = JsonSerializer.Serialize(new { app_id = "dock", pid = Environment.ProcessId, time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 });
                File.WriteAllText(heartbeat + ".tmp", body);
                File.Move(heartbeat + ".tmp", heartbeat, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    public void Dispose()
    {
        lock (gate) { disposed = true; timer.Dispose(); try { if (heartbeat is not null) File.Delete(heartbeat); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
