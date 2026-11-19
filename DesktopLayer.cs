using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace FloatingDock;

// Keep this window behind ordinary applications, while remaining above the desktop wallpaper.
public static class DesktopLayer
{
    static readonly HashSet<IntPtr> Surfaces=[];
    static readonly HashSet<IntPtr> PresentedSurfaces=[];
    static readonly List<IntPtr> EventHooks=[];
    static readonly WinEventProc NativeEvent=WindowEvent;
    static DispatcherTimer? refresh;
    static bool fallback;
    internal static event Action? ExternalForeground;
    public static void Attach(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        if (source is null||!Surfaces.Add(handle)) return;
        if(Surfaces.Count==1)StartTracking(window.Dispatcher);
        HwndSourceHook hook = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0046 && lParam != IntPtr.Zero && !PresentedSurfaces.Contains(handle)) // WM_WINDOWPOSCHANGING
            {
                var position = Marshal.PtrToStructure<WindowPosition>(lParam);
                position.InsertAfter = BottomAnchor(handle);
                position.Flags &= ~0x0004u; // allow the requested bottom Z order, preserving move/size flags
                Marshal.StructureToPtr(position, lParam, false);
            }
            return IntPtr.Zero;
        };
        source.AddHook(hook);
        void Lower(object? sender, EventArgs args) => LowerAll();
        window.Loaded += Lower;
        window.Activated += Lower;
        window.Closed += (_, _) =>
        {
            window.Loaded -= Lower; window.Activated -= Lower;
            if (!source.IsDisposed) source.RemoveHook(hook);
            Surfaces.Remove(handle);
            PresentedSurfaces.Remove(handle);
            if(Surfaces.Count==0){refresh?.Stop();refresh=null;foreach(var eventHook in EventHooks)UnhookWinEvent(eventHook);EventHooks.Clear();}
        };
    }

    internal static void SetShortcutPresentation(Window window, bool presented)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (!Surfaces.Contains(handle)) return;
        if (presented)
        {
            PresentedSurfaces.Add(handle);
            // HWND_TOPMOST makes the shortcut work across processes without activating the Dock.
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0213);
        }
        else if (PresentedSurfaces.Contains(handle))
        {
            // Leave the topmost band before restoring the ordinary desktop position.
            SetWindowPos(handle, new IntPtr(-2), 0, 0, 0, 0, 0x0213);
            PresentedSurfaces.Remove(handle);
            LowerAll();
        }
    }
    static void StartTracking(Dispatcher dispatcher)
    {
        fallback=false;refresh=new DispatcherTimer(DispatcherPriority.Background,dispatcher){Interval=TimeSpan.FromMilliseconds(35)};
        refresh.Tick+=(_,_)=>{if(!fallback)refresh?.Stop();LowerAll();};
        foreach(var range in new[]{(3u,3u),(0x8002u,0x8004u)}){var hook=SetWinEventHook(range.Item1,range.Item2,IntPtr.Zero,NativeEvent,0,0,0);if(hook!=IntPtr.Zero)EventHooks.Add(hook);}
        if(EventHooks.Count<2){fallback=true;refresh.Interval=TimeSpan.FromSeconds(1);refresh.Start();}
    }
    static void WindowEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time)
    {
        if(refresh is null||Surfaces.Contains(window)||(kind!=3&&kind!=0x8004&&(objectId!=0||childId!=0)))return;
        if(kind==3){bool owned=false;for(var current=window;current!=IntPtr.Zero;current=GetWindow(current,4))if(Surfaces.Contains(current)){owned=true;break;}
            if(!owned)try{ExternalForeground?.Invoke();}catch(Exception ex){LifecycleLog.Write("desktop-foreground "+ex.GetType().Name);}}
        if(!refresh.IsEnabled)refresh.Start();
    }
    static void LowerAll()
    {
        foreach(var window in Surfaces.ToArray()){
            if(!IsWindowVisible(window)||PresentedSurfaces.Contains(window))continue;
            var anchor=BottomAnchor(window);
            if(anchor==new IntPtr(-2)){if((GetWindowLong(window,-20)&0x8)!=0)SetWindowPos(window,anchor,0,0,0,0,0x0213);continue;}
            bool alreadyBelow=false;
            for(var above=GetWindow(window,3);above!=IntPtr.Zero;above=GetWindow(above,3))if(above==anchor){alreadyBelow=true;break;}
            if(!alreadyBelow)SetWindowPos(window,anchor,0,0,0,0,0x0213);
        }
    }

    static IntPtr BottomAnchor(IntPtr dock)
    {
        // HWND_BOTTOM can put a window behind Explorer's wallpaper. Stop above its desktop
        // windows instead, inserting after the lowest visible non-topmost application.
        var anchor = new IntPtr(-2); // HWND_NOTOPMOST when only the desktop is visible
        for (var current = GetTopWindow(IntPtr.Zero); current != IntPtr.Zero; current = GetWindow(current, 2))
        {
            if (current == dock || Surfaces.Contains(current)||!IsWindowVisible(current)) continue;
            var name = new StringBuilder(128); GetClassName(current, name, name.Capacity);
            if (name.ToString() is "Progman" or "WorkerW") break;
            if ((GetWindowLong(current, -20) & 0x8) == 0) anchor = current;
        }
        return anchor;
    }

    [StructLayout(LayoutKind.Sequential)] struct WindowPosition { public IntPtr Window, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    delegate void WinEventProc(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time);
    [DllImport("user32.dll")]static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEventProc callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")]static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr parent);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle, uint command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr handle, StringBuilder name, int maxCount);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr handle, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
