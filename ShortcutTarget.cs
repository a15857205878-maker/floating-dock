using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FloatingDock;

internal static class ShortcutTarget
{
    static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");

    internal static void Check(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            throw new FileNotFoundException("快捷方式已移动或删除。", path);
        if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path))
            return;

        object? link = null;
        try
        {
            link = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClass, true)!)!;
            ((IPersistFile)link).Load(path, 0);
            var shell = (IShellLinkW)link;
            var raw = new StringBuilder(260);
            // Special Shell targets can have no filesystem path. Let Explorer open those.
            if (shell.GetPath(raw, raw.Capacity, IntPtr.Zero, 4) != 0) return;
            var target = Environment.ExpandEnvironmentVariables(raw.ToString());
            if (!Path.IsPathFullyQualified(target) || target.StartsWith(@"\\", StringComparison.Ordinal)
                || new DriveInfo(Path.GetPathRoot(target)!).DriveType != DriveType.Fixed)
                return;

            if (File.Exists(target) || Directory.Exists(target)) return;
            // Installer shortcuts can be valid before their advertised target has been installed.
            if (MsiGetShortcutTarget(path, new StringBuilder(39), new StringBuilder(256), new StringBuilder(39)) == 0)
                return;

            // Try the shell's link tracking without UI, disk-wide heuristics, or rewriting the shortcut.
            const uint noUiOrUpdate = (650u << 16) | 0x01 | 0x08 | 0x10;
            if (shell.Resolve(IntPtr.Zero, noUiOrUpdate) != 0
                && !File.Exists(target) && !Directory.Exists(target))
                throw new FileNotFoundException("快捷方式指向的目标已移动或删除。", path);
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "MsiGetShortcutTargetW")]
    static extern uint MsiGetShortcutTarget(string path, StringBuilder product, StringBuilder feature, StringBuilder component);

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        [PreserveSig] int GetPath([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr findData, uint flags);
        [PreserveSig] int GetIdList(out IntPtr list);
        [PreserveSig] int SetIdList(IntPtr list);
        [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out short hotkey);
        [PreserveSig] int SetHotkey(short hotkey);
        [PreserveSig] int GetShowCommand(out int command);
        [PreserveSig] int SetShowCommand(int command);
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
    }
}
