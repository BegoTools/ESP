using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ESPWorkbench
{
    public sealed class CommandResult
    {
        public byte[] Bytes;
        public string Error;
        public int ExitCode;
        public string Text { get { return Encoding.UTF8.GetString(Bytes).Trim(); } }
        public void EnsureSuccess() { if (ExitCode != 0) throw new IOException("Command failed ("+ExitCode+"): "+Error+" "+Text); }
    }
    public static class Commands
    {
        // Windows argv quoting, including embedded quotes and trailing backslashes.
        public static string Quote(string value)
        {
            StringBuilder b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes*2+1); b.Append(c); slashes = 0; continue; }
                b.Append('\\', slashes); slashes = 0; b.Append(c);
            }
            b.Append('\\', slashes*2); return b.Append('"').ToString();
        }
        public static CommandResult Run(string executable, IEnumerable<string> args, int timeout, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (Process p = new Process())
            using (MemoryStream buffer = new MemoryStream())
            {
                p.StartInfo = new ProcessStartInfo(executable, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                if (!p.Start()) throw new IOException("Unable to start "+executable);
                Task output = p.StandardOutput.BaseStream.CopyToAsync(buffer);
                Task<string> error = p.StandardError.ReadToEndAsync();
                Stopwatch watch = Stopwatch.StartNew();
                while (!p.WaitForExit(40))
                {
                    if (token.IsCancellationRequested || watch.ElapsedMilliseconds > timeout)
                    {
                        try { p.Kill(); } catch (InvalidOperationException) { }
                        p.WaitForExit(2000);
                        try { Task.WaitAll(new Task[] { output, error }, 2000); } catch (AggregateException) { }
                        token.ThrowIfCancellationRequested(); throw new TimeoutException("Command timed out after "+timeout+" ms: "+Path.GetFileName(executable));
                    }
                }
                if (!Task.WaitAll(new Task[] { output, error }, 2000)) throw new IOException("Process output did not close.");
                return new CommandResult { Bytes = buffer.ToArray(), Error = error.Result.Trim(), ExitCode = p.ExitCode };
            }
        }
    }
    public sealed class Device
    {
        public string Serial, State, Details;
        public override string ToString() { return Serial+"  ["+State+"] "+Details; }
    }
    public enum PrivilegeMode { Unavailable, Shell, Su, Txperm }
    public sealed class Adb
    {
        public readonly string Executable, Serial;
        public PrivilegeMode Privilege { get; private set; }
        public Adb(string exe, string serial) { Executable = exe; Serial = serial; }
        public CommandResult Run(CancellationToken token, params string[] args)
        {
            List<string> all = new List<string>();
            if (!String.IsNullOrEmpty(Serial)) { all.Add("-s"); all.Add(Serial); }
            all.AddRange(args); return Commands.Run(Executable, all, 10000, token);
        }
        public string Shell(string command, CancellationToken token)
        {
            CommandResult r = Run(token, "shell", command); r.EnsureSuccess();
            if (r.Text.StartsWith("error:", StringComparison.OrdinalIgnoreCase)) throw new IOException(r.Text);
            return r.Text;
        }
        public bool DetectRoot(CancellationToken token)
        {
            Privilege=PrivilegeMode.Unavailable;
            foreach(PrivilegeMode mode in new[] { PrivilegeMode.Shell,PrivilegeMode.Su,PrivilegeMode.Txperm })
            {
                try
                {
                    if(Regex.IsMatch(Shell(BuildRootCommand(mode,"id",false),token),@"\buid=0\b")) { Privilege=mode; return true; }
                }
                catch(IOException) { }
            }
            return false;
        }
        public static string BuildRootCommand(PrivilegeMode mode,string command,bool processAccess)
        {
            if(mode==PrivilegeMode.Unavailable) throw new IOException("No existing privileged command facility was found.");
            if(mode==PrivilegeMode.Txperm&&processAccess)
            {
                // GameLoop's existing txperm retains the shell SELinux domain. Open proc handles
                // during a bounded bootstrap; the native reader restores enforcing before reads.
                // Both a shell trap and an independent two-second guard restore on failed startup.
                command="esp_restore=0\n"+
                    "if [ \"$(getenforce)\" = Enforcing ]; then\n"+
                    "  esp_restore=1\n  export ESP_RESTORE_ENFORCING=1\n"+
                    "  trap 'setenforce 1' EXIT HUP INT TERM\n"+
                    "  (sleep 2; setenforce 1) </dev/null >/dev/null 2>&1 &\n"+
                    "  setenforce 0 || exit 70\nfi\n"+command+"\nesp_result=$?\n"+
                    "if [ \"$esp_restore\" = 1 ]; then setenforce 1 || exit 71; fi\n"+
                    "trap - EXIT HUP INT TERM\nexit \"$esp_result\"";
            }
            if(mode==PrivilegeMode.Su) return "su -c "+ShellQuote(command);
            return (mode==PrivilegeMode.Txperm?"/system/xbin/txperm 0 ":"")+"/system/bin/sh -c "+ShellQuote(command);
        }
        public string PrivilegedCommand(string command,bool processAccess=false) { return BuildRootCommand(Privilege,command,processAccess); }
        public string RootShell(string command,CancellationToken token,bool processAccess=false) { return Shell(PrivilegedCommand(command,processAccess),token); }
        public string ReadMaps(int pid,CancellationToken token)
        {
            if(pid<1)throw new ArgumentException("Invalid process ID.");
            return RootShell("cat /proc/"+pid+"/maps",token,true);
        }
        public static string ShellQuote(string s) { return "'"+s.Replace("'", "'\"'\"'")+"'"; }
        public static List<Device> ParseDevices(string text)
        {
            List<Device> devices = new List<Device>();
            foreach (string line in text.Split('\n'))
            {
                Match m = Regex.Match(line.Trim(), @"^(\S+)\s+(device|offline|unauthorized|recovery|sideload|no permissions)\b\s*(.*)$");
                if (m.Success) devices.Add(new Device { Serial = m.Groups[1].Value, State = m.Groups[2].Value, Details = m.Groups[3].Value });
            }
            return devices;
        }
        public static string Find()
        {
            foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            { try { string p = Path.Combine(dir.Trim('"'), "adb.exe"); if (File.Exists(p)) return p; } catch (ArgumentException) { } }
            return "adb.exe";
        }
    }
    public sealed class GameInfo
    {
        public int Pid;
        public string VersionName, VersionCode, Abi, AndroidVersion;
        public bool Root;
        public string Maps;
        public static GameInfo Inspect(Adb adb, string package, bool includeMaps, CancellationToken token)
        {
            if (!Regex.IsMatch(package ?? "", @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+$")) throw new ArgumentException("Invalid package.");
            string dump = adb.Shell("dumpsys package "+package, token);
            Match version = Regex.Match(dump, @"versionName=(\S+)"), code = Regex.Match(dump, @"versionCode=(\d+)"), abi = Regex.Match(dump, @"primaryCpuAbi=(\S+)");
            if (!version.Success || !code.Success) throw new IOException("Package not installed or version unavailable: "+package);
            string pidText = adb.Shell("pidof "+package, token);
            int pid;
            if (!Int32.TryParse(pidText.Split(' ')[0], out pid) || pid < 1) throw new IOException("The package is installed but not running. Open the game first.");
            GameInfo info = new GameInfo { Pid = pid, VersionName = version.Groups[1].Value, VersionCode = code.Groups[1].Value, Abi = abi.Groups[1].Value, AndroidVersion = adb.Shell("getprop ro.build.version.release", token) };
            info.Root=adb.DetectRoot(token);
            if (includeMaps)
            {
                if (!info.Root) throw new IOException("Root access is unavailable. An existing privileged shell, su, or GameLoop txperm is required.");
                info.Maps = adb.ReadMaps(pid,token);
                if (MapRegion.Parse(info.Maps).Count == 0) throw new IOException("Process maps are unavailable or access was denied.");
            }
            return info;
        }
    }
    public static class GameCompatibility
    {
        public static List<string> Problems(Profile profile, GameInfo info)
        {
            List<string> problems=new List<string>();
            if (profile.VersionName!=info.VersionName || profile.VersionCode!=info.VersionCode)
                problems.Add("Profile/build mismatch: installed "+info.VersionName+" ("+info.VersionCode+"); profile "+profile.VersionName+" ("+profile.VersionCode+"). A verified profile and compatible reader are required for this build.");
            if (!info.Root)
                problems.Add("Root access unavailable: the reader needs an existing privileged command facility (su, root shell, or GameLoop txperm). ADB connection alone is insufficient.");
            string abi=info.Abi??"";
            if ((abi.Contains("64") && profile.PointerSize!=8) || (!abi.Contains("64") && abi!="null" && abi.Length>0 && profile.PointerSize!=4))
                problems.Add("Package ABI does not match the profile pointer width.");
            return problems;
        }
        public static void Ensure(Profile profile, GameInfo info, string emulator)
        {
            List<string> problems=Problems(profile,info);
            if(problems.Count>0) throw new IOException(emulator+" / Live unavailable:\r\n"+String.Join("\r\n",problems));
        }
    }
    public interface IMemory
    {
        byte[] Read(ulong address, int count);
        void NewFrame();
    }
    public sealed class MemoryTransportException : Exception
    {
        public MemoryTransportException(string message) : base(message) { }
    }
    public sealed class AndroidMemory : IMemory
    {
        private readonly Adb adb;
        private readonly int pid;
        private readonly List<MapRegion> maps;
        private readonly CancellationToken token;
        private readonly Dictionary<ulong, byte[]> cache = new Dictionary<ulong, byte[]>();
        public AndroidMemory(Adb adb, int pid, List<MapRegion> maps, CancellationToken token) { this.adb = adb; this.pid = pid; this.maps = maps; this.token = token; }
        public void NewFrame() { cache.Clear(); }
        public byte[] Read(ulong address, int count)
        {
            if (count < 1 || count > 131072 || address > UInt64.MaxValue-(ulong)count) throw new IOException("Invalid memory read range.");
            byte[] result = new byte[count]; int copied = 0;
            while (copied < count)
            {
                token.ThrowIfCancellationRequested();
                ulong current = address+(ulong)copied;
                MapRegion region = maps.FirstOrDefault(m => m.Start <= current && current < m.End && m.Permissions.StartsWith("r", StringComparison.Ordinal));
                if (region == null) throw new IOException("Address is not in a readable map: 0x"+current.ToString("X"));
                ulong page = current & ~4095UL;
                byte[] data;
                if (!cache.TryGetValue(page, out data))
                {
                    // dd operates on a single page, never on a multi-gigabyte byte-by-byte skip.
                    string command = "/system/bin/toybox dd if=/proc/"+pid+"/mem bs=4096 skip="+(page/4096).ToString(System.Globalization.CultureInfo.InvariantCulture)+" count=1 2>/dev/null | /system/bin/toybox base64";
                    CommandResult r = adb.Run(token, "exec-out", adb.PrivilegedCommand(command,true));
                    try { data=Convert.FromBase64String(r.Text); } catch (FormatException) { throw new MemoryTransportException("Invalid memory transport response at 0x"+page.ToString("X")+". Check su access and toybox base64."); }
                    if (r.ExitCode != 0 || data.Length != 4096) throw new MemoryTransportException("Memory read failed at 0x"+page.ToString("X")+" (decoded "+data.Length+" of 4096 bytes). Access denied, incompatible toybox, or process exited.");
                    cache.Add(page, data);
                }
                int offset = (int)(current-page);
                int length = Math.Min(count-copied, Math.Min(4096-offset, (int)Math.Min(4096UL, region.End-current)));
                Buffer.BlockCopy(data, offset, result, copied, length); copied += length;
            }
            return result;
        }
    }
    public sealed class MemoryValues
    {
        private readonly IMemory memory;
        private readonly int pointerSize;
        public MemoryValues(IMemory memory, int pointerSize) { this.memory = memory; this.pointerSize = pointerSize; }
        public ulong Pointer(ulong a) { byte[] b = memory.Read(a, pointerSize); ulong p = pointerSize == 8 ? BitConverter.ToUInt64(b,0) : BitConverter.ToUInt32(b,0); if (p < 4096) throw new IOException("Null or invalid pointer."); return p; }
        public ulong Resolve(ulong root, string[] path)
        {
            ulong address = checked(root+Profile.Offset(path[0]));
            for (int i = 1; i < path.Length; i++) address = checked(Pointer(address)+Profile.Offset(path[i]));
            return address;
        }
        public int Int(ulong a) { return BitConverter.ToInt32(memory.Read(a,4),0); }
        public float Float(ulong a) { return BitConverter.ToSingle(memory.Read(a,4),0); }
        public Vec3 Vector(ulong a) { byte[] b = memory.Read(a,12); return new Vec3(BitConverter.ToSingle(b,0), BitConverter.ToSingle(b,4), BitConverter.ToSingle(b,8)); }
        public float[] Matrix(ulong a) { byte[] b = memory.Read(a,64); float[] m = new float[16]; Buffer.BlockCopy(b,0,m,0,64); if (m.Any(f => !Vec3.Good(f)) || m.All(f => f == 0)) throw new IOException("View-projection matrix is invalid."); return m; }
    }
    public sealed class GameReader
    {
        private readonly Profile profile;
        private readonly IMemory memory;
        private readonly MemoryValues values;
        private readonly ulong moduleBase;
        public GameReader(Profile p, IMemory memory, ulong moduleBase) { profile = p; this.memory = memory; values = new MemoryValues(memory,p.PointerSize); this.moduleBase = moduleBase; }
        public Frame Read(CancellationToken token)
        {
            Stopwatch sw = Stopwatch.StartNew(); memory.NewFrame();
            int count = values.Int(values.Resolve(moduleBase, profile.ActorCountPath));
            if (count < 0 || count > profile.MaxActors) throw new IOException("Invalid actor count: "+count+". Check the profile; reading stopped.");
            Frame f = new Frame { Matrix = values.Matrix(values.Resolve(moduleBase,profile.MatrixPath)), ColumnMajor = profile.ColumnMajor, Origin = values.Vector(values.Resolve(moduleBase,profile.OriginPath)), UnitsPerMeter = profile.UnitsPerMeter, Source = "LIVE / "+profile.Name };
            if (!f.Origin.Finite) throw new IOException("Invalid local position.");
            if (Profile.Has(profile.LocalTeamPath)) f.LocalTeam = values.Int(values.Resolve(moduleBase,profile.LocalTeamPath));
            List<Actor> actors = new List<Actor>();
            if (count > 0)
            {
                ulong array = values.Pointer(values.Resolve(moduleBase,profile.ActorDataPath));
                // Snapshot pointers in a bounded batch; per-frame page cache covers nearby fields.
                byte[] pointers = memory.Read(array,checked(count*profile.PointerSize));
                for (int i = 0; i < count && actors.Count < profile.MaxDisplayed; i++)
                {
                    token.ThrowIfCancellationRequested();
                    ulong actor = profile.PointerSize == 8 ? BitConverter.ToUInt64(pointers,i*8) : BitConverter.ToUInt32(pointers,i*4);
                    if (actor < 4096) continue;
                    try
                    {
                        if (Profile.Has(profile.PlayerMarkerPath) && values.Int(values.Resolve(actor,profile.PlayerMarkerPath)) != profile.PlayerMarkerValue) continue;
                        float health = values.Float(values.Resolve(actor,profile.HealthPath));
                        if (!Vec3.Good(health) || health <= 0 || health > profile.MaxHealth*1.1f) continue;
                        Vec3 pos = values.Vector(values.Resolve(actor,profile.PositionPath));
                        if (!pos.Finite) continue;
                        actors.Add(new Actor { Name = "Actor "+actor.ToString("X"), Health = health, MaxHealth = profile.MaxHealth, Height = profile.ActorHeight, Position = pos, Team = values.Int(values.Resolve(actor,profile.TeamPath)) });
                    }
                    catch (IOException) { /* Actors may despawn during a snapshot. */ }
                }
            }
            f.Actors = actors.ToArray(); f.CapturedUtc = DateTime.UtcNow; f.ReadMilliseconds = sw.Elapsed.TotalMilliseconds;
            return f;
        }
    }
}
