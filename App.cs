using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace FloatingDock;

public sealed class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        bool showRequested=args.Contains("--show",StringComparer.Ordinal);
        bool inJob=DesktopLauncher.IsJobBound;
        LifecycleLog.Write("starting job-bound="+inJob);
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>LifecycleLog.Write("process-exit");
        AppDomain.CurrentDomain.UnhandledException+=(_,e)=>LifecycleLog.Write("unhandled "+e.ExceptionObject);
        if(inJob&&!args.Contains("--desktop-session",StringComparer.Ordinal))
        {
            try
            {
                DesktopLauncher.Open(Path.Combine(AppContext.BaseDirectory,"FloatingDock.exe"),"--desktop-session"+(showRequested?" --show":""),AppContext.BaseDirectory);
                LifecycleLog.Write("handed-off-to-explorer");return;
            }
            catch(Exception ex){LifecycleLog.Write("desktop-handoff-failed "+ex);}
        }
        var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];
        using var single = new Mutex(true, @"Local\FloatingDock-" + identity, out bool first);
        if (!first) {
            if(showRequested && InstanceActivation.Request(identity)){LifecycleLog.Write("activation-sent");return;}
            MessageBox.Show("悬浮控制中心已经在运行。\n可在屏幕上方或下方找到它。", "悬浮控制中心"); return;
        }
        var app = new App { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var path = Path.Combine(AppContext.BaseDirectory, "data", "settings.json");
        app.DispatcherUnhandledException += (_, e) =>
        {
            LifecycleLog.Write("dispatcher-error "+e.Exception);
            try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "error.log"), DateTimeOffset.Now + " " + e.Exception + Environment.NewLine); } catch { }
            MessageBox.Show("遇到错误：" + e.Exception.Message, "悬浮控制中心", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
        try
        {
            var existed = File.Exists(path);
            DockState state;
            try { state = DockStore.Load(path); }
            catch (InvalidDataException)
            {
                var backup = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(path, backup, false);
                state = new DockState();
                MessageBox.Show("配置文件无法读取，已保留一份副本。\n将重新导入桌面快捷方式。", "悬浮控制中心");
                existed = false;
            }
            if (!existed) DockCatalog.Merge(state, NativeServices.DesktopShortcuts());
            var window=new DockWindow(state,path);
            bool shortcutReady = false;
            int pendingShortcuts = showRequested ? 1 : 0;
            void ToggleDock()
            {
                if (app.Dispatcher.HasShutdownStarted) return;
                // Window activation and rendering may keep the dispatcher from becoming idle.
                _ = app.Dispatcher.InvokeAsync(() =>
                {
                    if (app.Dispatcher.HasShutdownStarted) return;
                    if (shortcutReady) window.ToggleFromShortcut();
                    else pendingShortcuts++;
                }, DispatcherPriority.Normal);
            }
            window.Loaded += (_, _) =>
            {
                shortcutReady = true;
                for (; pendingShortcuts > 0; pendingShortcuts--) ToggleDock();
                LifecycleLog.Write("window-ready job-bound="+DesktopLauncher.IsJobBound);
            };
            using var activation=new InstanceActivation(identity,ToggleDock);
            window.Closing+=(_,_)=>LifecycleLog.Write("window-closing");
            app.SessionEnding+=(_,_)=>LifecycleLog.Write("windows-session-ending");
            using var appLink = new AppLinkPresence();
            app.Run(window);
        }
        catch (Exception ex) { LifecycleLog.Write("startup-error "+ex);MessageBox.Show("无法启动悬浮控制中心：\n" + ex.Message, "悬浮控制中心", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
