using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ESPWorkbench
{
    public enum EmulatorKind { LDPlayer, GameLoop }

    public sealed class EmulatorConnection
    {
        public string AdbPath = "", ConsolePath = "", Serial = "", ProfilePath = "";
        public string Endpoint = "127.0.0.1:5555";
    }

    public static class Emulators
    {
        public static string Name(EmulatorKind kind) { return kind == EmulatorKind.GameLoop ? "GameLoop" : "LDPlayer 14"; }

        public static string ValidateEndpoint(string value)
        {
            // Only attach to a local emulator; never turn a host name into an arbitrary network connection.
            Match m = Regex.Match((value ?? "").Trim(), @"^(127\.0\.0\.1|localhost):([0-9]{1,5})$", RegexOptions.IgnoreCase);
            int port;
            if (!m.Success || !Int32.TryParse(m.Groups[2].Value, out port) || port < 1 || port > 65535)
                throw new IOException("GameLoop ADB address must be a local address such as 127.0.0.1:5555.");
            return "127.0.0.1:" + port;
        }

        public static string FindConsole()
        {
            foreach (string dir in ProcessDirectories(new[] { "dnplayer" }))
                foreach (string name in new[] { "ldconsole.exe", "dnconsole.exe" })
                { string file = Path.Combine(dir, name); if (File.Exists(file)) return file; }
            foreach (DriveInfo drive in FixedDrives())
                foreach (string folder in new[] { @"LDPlayer\LDPlayer14", @"LDPlayer\LDPlayer9", @"Program Files\LDPlayer\LDPlayer14" })
                    foreach (string name in new[] { "ldconsole.exe", "dnconsole.exe" })
                    { string file = Path.Combine(drive.RootDirectory.FullName, folder, name); if (File.Exists(file)) return file; }
            return "";
        }

        public static string FindAdb(EmulatorKind kind, string console)
        {
            if (kind == EmulatorKind.LDPlayer)
            {
                string bundled = String.IsNullOrEmpty(console) ? "" : Path.Combine(Path.GetDirectoryName(console), "adb.exe");
                return File.Exists(bundled) ? bundled : Adb.Find();
            }
            foreach (string dir in ProcessDirectories(new[] { "GameLoopEmulator", "GameLoop", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator" }))
            { string file = Path.Combine(dir, "adb.exe"); if (File.Exists(file)) return file; }
            foreach (DriveInfo drive in FixedDrives())
                foreach (string folder in new[] { @"Program Files\Tencent\GameLoop\Application", @"Program Files (x86)\Tencent\GameLoop\Application", @"Tencent\GameLoop\Application", @"Program Files\TxGameAssistant\ui", @"Program Files (x86)\TxGameAssistant\ui", @"TxGameAssistant\ui" })
                {
                    string root = Path.Combine(drive.RootDirectory.FullName, folder);
                    string direct = Path.Combine(root, "adb.exe"); if (File.Exists(direct)) return direct;
                    if (!Directory.Exists(root)) continue;
                    try
                    {
                        foreach (string dir in Directory.GetDirectories(root).OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)))
                        { string file = Path.Combine(dir, "adb.exe"); if (File.Exists(file)) return file; }
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            return Adb.Find();
        }

        private static IEnumerable<DriveInfo> FixedDrives() { return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady); }
        private static IEnumerable<string> ProcessDirectories(string[] names)
        {
            List<string> directories = new List<string>();
            foreach (string name in names)
                foreach (Process process in Process.GetProcessesByName(name))
                {
                    try { directories.Add(Path.GetDirectoryName(process.MainModule.FileName)); }
                    catch (System.ComponentModel.Win32Exception) { }
                    catch (InvalidOperationException) { }
                    finally { process.Dispose(); }
                }
            return directories.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static List<Instance> GameLoopWindows()
        {
            HashSet<int> pids = new HashSet<int>();
            foreach (string name in new[] { "GameLoopEmulator", "AndroidEmulatorEn", "AndroidEmulatorEx", "AndroidEmulator" })
                foreach (Process process in Process.GetProcessesByName(name))
                { using (process) { pids.Add(process.Id); } }
            List<Instance> result = new List<Instance>();
            Native.EnumWindows(delegate(IntPtr window, IntPtr unused) {
                uint pid; Native.GetWindowThreadProcessId(window, out pid);
                if (!pids.Contains((int)pid) || !Native.IsWindowVisible(window)) return true;
                string title = Native.WindowText(window);
                if (String.IsNullOrWhiteSpace(title)) return true;
                List<IntPtr> children = new List<IntPtr>();
                Native.EnumChildWindows(window, delegate(IntPtr child, IntPtr ignored) { children.Add(child); return true; }, IntPtr.Zero);
                // The rendering child excludes the emulator's title bar and side toolbar.
                IntPtr render = children.Where(c => Native.IsWindowVisible(c) && IsRenderSurface(Native.WindowClass(c), Native.WindowText(c)))
                    .OrderByDescending(c => Area(Native.ClientBounds(c))).FirstOrDefault();
                result.Add(new Instance { Index = result.Count, Title = title, Window = render, HostWindow = window, Emulator = EmulatorKind.GameLoop });
                return true;
            }, IntPtr.Zero);
            return result.OrderBy(i => i.Window==IntPtr.Zero?1:0).ToList();
        }

        public static bool IsRenderSurface(string className, string title)
        {
            return String.Equals(className, "RenderWindow", StringComparison.OrdinalIgnoreCase)
                || String.Equals(className, "AEngineRenderWindowClass", StringComparison.OrdinalIgnoreCase)
                || (String.Equals(className, "subWin", StringComparison.OrdinalIgnoreCase) && String.Equals(title, "sub", StringComparison.OrdinalIgnoreCase))
                || String.Equals(title, "RenderWindow", StringComparison.OrdinalIgnoreCase)
                || String.Equals(title, "AEngineRenderWindow", StringComparison.OrdinalIgnoreCase);
        }
        private static long Area(Rectangle bounds) { return (long)bounds.Width * bounds.Height; }

        public static string WindowReport()
        {
            StringBuilder report = new StringBuilder();
            foreach (Instance instance in GameLoopWindows())
            {
                report.AppendLine(instance.Title + " host=" + instance.HostWindow + " render=" + instance.Window);
                Action<IntPtr> describe = delegate(IntPtr window) { report.AppendLine(window + " / " + Native.WindowClass(window) + " / " + Native.WindowText(window) + " / " + Native.ClientBounds(window) + " / visible=" + Native.IsWindowVisible(window)); };
                describe(instance.HostWindow);
                Native.EnumChildWindows(instance.HostWindow, delegate(IntPtr child, IntPtr ignored) { describe(child); return true; }, IntPtr.Zero);
            }
            return report.ToString();
        }
    }
}
