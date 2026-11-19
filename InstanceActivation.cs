namespace FloatingDock;

// The launcher shortcut works even after the application exits. A running owner
// receives a named event instead of creating another window or blocking on a dialog.
public sealed class InstanceActivation : IDisposable
{
    readonly EventWaitHandle signal;
    readonly RegisteredWaitHandle registration;
    int disposed;
    static string Name(string identity) => @"Local\FloatingDock-Activate-" + identity;

    public InstanceActivation(string identity, Action activate)
    {
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, Name(identity));
        registration = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            if (Volatile.Read(ref disposed) == 0) activate();
        }, null, Timeout.Infinite, false);
    }

    public static bool Request(string identity, int waitMilliseconds = 1000)
    {
        var until = Environment.TickCount64 + waitMilliseconds;
        do
        {
            try { using var handle = EventWaitHandle.OpenExisting(Name(identity)); return handle.Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            if (Environment.TickCount64 >= until) return false;
            Thread.Sleep(25);
        } while (true);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        registration.Unregister(null); signal.Dispose();
    }
}
