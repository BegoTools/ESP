using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    public sealed class NativeActorData
    {
        public string Address,Name,Class,Kind,LootGroup,ItemId;
        public Vec3 Position;
        public float Health,MaxHealth,Height;
        public float Fuel,MaxFuel;
        public bool HasHealth,HasFuel;
        public bool? IsBot,IsVisible;
        public bool? IsDead;
        public float? NearDeathBreath;
        public string VisibilitySource;
        public int Quantity;
        public int Team;
    }
    public sealed class NativeSnapshot
    {
        public string Error,LocalPawn,Session;
        public int ReaderPid,LocalTeam,ActorCount;
        public bool? LocalScoped,LocalFiring;
        public Vec3 CameraPosition,CameraRotation,Origin;
        public float Fov;
        public double ReadMilliseconds;
        public NativeActorData[] Actors;
        public NativeActorData[] StaticActors;
        public static NativeSnapshot Parse(string json)
        {
            if (json.Length>2*1024*1024) throw new InvalidDataException(L.Text("Native snapshot exceeds size limit."));
            NativeSnapshot s=new JavaScriptSerializer { MaxJsonLength=2*1024*1024 }.Deserialize<NativeSnapshot>(json);
            if (s==null) throw new InvalidDataException(L.Text("Empty native snapshot."));
            if (!String.IsNullOrEmpty(s.Error)) throw new IOException(s.Error);
            if (!s.CameraPosition.Finite || !s.CameraRotation.Finite || !s.Origin.Finite || !Vec3.Good(s.Fov) || s.Fov<5 || s.Fov>175 || s.ActorCount<0 || s.ActorCount>16384 || s.Actors==null || s.Actors.Length+(s.StaticActors==null?0:s.StaticActors.Length)>16384) throw new InvalidDataException(L.Text("Invalid native camera or actor data."));
            return s;
        }
        public Frame ToFrame()
        {
            return new Frame { CameraMode=true,CameraPosition=CameraPosition,CameraRotation=CameraRotation,Fov=Fov,Origin=Origin,LocalTeam=LocalTeam,LocalScoped=LocalScoped,LocalFiring=LocalFiring,TotalActorCount=ActorCount,ReadMilliseconds=ReadMilliseconds,Source="LIVE / PUBG Global 4.6.0",Actors=ConvertActors(Actors.Concat(StaticActors??new NativeActorData[0]),LocalPawn) };
        }
        public static Actor[] ConvertActors(System.Collections.Generic.IEnumerable<NativeActorData> actors,string localPawn)
        {
            return actors.Where(a=>a.Address!=localPawn&&a.Position.Finite&&((a.Kind!="Character"&&a.Kind!="TrainingTarget")||CharacterLife.Retain(a.Health,a.MaxHealth,a.IsDead,a.NearDeathBreath))).Select(a=>new Actor { Address=a.Address,Kind=a.Kind,LootGroup=a.LootGroup,ItemId=a.ItemId,Quantity=a.Quantity,IsBot=a.IsBot,IsVisible=a.IsVisible,VisibilitySource=a.VisibilitySource,IsDead=a.IsDead,NearDeathBreath=a.NearDeathBreath,HasHealth=a.HasHealth||a.MaxHealth>0,HasFuel=a.HasFuel,Fuel=a.Fuel,MaxFuel=a.MaxFuel,Name=String.IsNullOrWhiteSpace(a.Name)?a.Class:a.Name,Position=new Vec3(a.Position.X,a.Position.Y,a.Position.Z-(a.Height>0?a.Height/2:0)),Height=a.Height,Health=a.Health,MaxHealth=a.MaxHealth,Team=a.Team }).ToArray();
        }
    }
    public sealed class NativeReader
    {
        public const string RemotePath="/data/local/tmp/esp-workbench-reader";
        private readonly Adb adb;
        private readonly int gamePid;
        private readonly ulong moduleBase;
        private readonly string session=Guid.NewGuid().ToString("N");
        private int helperPid;
        public NativeReader(Adb adb,int gamePid,ulong moduleBase) { this.adb=adb; this.gamePid=gamePid; this.moduleBase=moduleBase; }
        public static void Prepare(Adb adb,Profile profile,GameInfo info,System.Collections.Generic.List<MapRegion> maps,CancellationToken token)
        {
            if (adb.Shell("uname -m",token).Trim()!="x86_64") throw new IOException(L.Text("This native helper requires an x86_64 Android kernel on the selected emulator."));
            MapRegion image=maps.First(m=>m.Path.EndsWith("/"+profile.Module,StringComparison.Ordinal)&&m.FileOffset==0);
            string hash=adb.RootShell("/system/bin/toybox sha256sum "+Adb.ShellQuote(image.Path),token).Split(new[] {' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!String.Equals(hash,profile.ModuleSha256,StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Text("Library SHA-256 differs from the inspected build. Reader stopped before using offsets."));
            string payload=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native","esp-reader");
            if (!File.Exists(payload)) throw new FileNotFoundException(L.Text("Native reader binary is missing."),payload);
            CommandResult pushed=adb.Run(token,"push",payload,RemotePath); pushed.EnsureSuccess();
            adb.Shell("chmod 700 "+RemotePath,token);
            string remoteHash=adb.Shell("/system/bin/toybox sha256sum "+RemotePath,token).Split(new[] {' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            string expected;
            using (FileStream stream=File.OpenRead(payload)) using (SHA256 sha=SHA256.Create()) expected=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            if (!String.Equals(remoteHash,expected,StringComparison.OrdinalIgnoreCase)) throw new IOException(L.Text("Native helper transfer verification failed."));
        }
        private string Command(bool once) { return adb.PrivilegedCommand(RemotePath+" "+gamePid.ToString(CultureInfo.InvariantCulture)+" "+moduleBase.ToString("x",CultureInfo.InvariantCulture)+(once?" --once":" --keepalive --delta")+" --session="+session,true); }
        public NativeSnapshot Once(CancellationToken token)
        {
            CommandResult r=adb.Run(token,"exec-out",Command(true)); r.EnsureSuccess();
            string line=r.Text.Split('\n').FirstOrDefault(s=>s.TrimStart().StartsWith("{",StringComparison.Ordinal));
            if (line==null) throw new IOException(L.Format("Native reader did not return a snapshot: {0}",r.Text));
            return NativeSnapshot.Parse(line);
        }
        public async Task Stream(Action<Frame> publish,CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using (Process process=new Process())
                {
                    process.StartInfo=new ProcessStartInfo(adb.Executable,String.Join(" ",new[] {"-s",adb.Serial,"shell",Command(false)}.Select(Commands.Quote))) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
                    if (!process.Start()) throw new IOException(L.Text("Unable to start native reader transport."));
                    Task<string> stderr=process.StandardError.ReadToEndAsync();
                    Stopwatch arrival=Stopwatch.StartNew();System.Collections.Generic.Queue<double> rateWindow=new System.Collections.Generic.Queue<double>(); Actor[] staticCache=new Actor[0]; double lastStatic=-10000;
                    try
                    {
                        await process.StandardInput.WriteLineAsync("PING"); await process.StandardInput.FlushAsync(); double heartbeat=0;
                        while (!token.IsCancellationRequested)
                        {
                            Task<string> next=process.StandardOutput.ReadLineAsync();
                            Task winner=await Task.WhenAny(next,Task.Delay(4000,token));
                            token.ThrowIfCancellationRequested();
                            if (winner!=next) throw new TimeoutException(L.Text("No native frame for four seconds. Reader stopped."));
                            string line=await next;
                            if (line==null) break;
                            line=line.Trim(); if (!line.StartsWith("{",StringComparison.Ordinal)) continue;
                            NativeSnapshot snapshot=NativeSnapshot.Parse(line);
                            if (snapshot.Session!=session || snapshot.ReaderPid<1) throw new InvalidDataException(L.Text("Native reader session identity mismatch."));
                            helperPid=snapshot.ReaderPid;
                            double timestamp=arrival.Elapsed.TotalMilliseconds;
                            if(snapshot.StaticActors!=null) { staticCache=NativeSnapshot.ConvertActors(snapshot.StaticActors,snapshot.LocalPawn);lastStatic=timestamp;snapshot.StaticActors=null; }
                            Frame frame=snapshot.ToFrame();if(timestamp-lastStatic<1500&&staticCache.Length>0)frame.Actors=frame.Actors.Concat(staticCache).ToArray();
                            if(timestamp-heartbeat>=500) { await process.StandardInput.WriteLineAsync("PING"); await process.StandardInput.FlushAsync(); heartbeat=timestamp; }
                            rateWindow.Enqueue(timestamp);while(rateWindow.Count>1&&rateWindow.Peek()<timestamp-1000)rateWindow.Dequeue();
                            frame.ReadHertz=rateWindow.Count>1&&timestamp>rateWindow.Peek()?(rateWindow.Count-1)*1000/(timestamp-rateWindow.Peek()):0; token.ThrowIfCancellationRequested(); publish(frame);
                        }
                        if (!token.IsCancellationRequested)
                        {
                            process.WaitForExit(1000);
                            if (!process.HasExited || process.ExitCode!=0) throw new IOException(L.Format("Native helper exited unexpectedly. {0}",stderr.IsCompleted?stderr.Result:""));
                        }
                    }
                    finally
                    {
                        try { process.StandardInput.Close(); } catch (InvalidOperationException) { }
                        if (!process.HasExited) { try { process.Kill(); } catch (InvalidOperationException) { } process.WaitForExit(2000); }
                        Cleanup();
                    }
                }
            }
        }
        private void Cleanup()
        {
            if (helperPid<1) return;
            try
            {
                // Only terminate this helper process after matching both path and unique session token.
                string cmdline=adb.RootShell("cat /proc/"+helperPid+"/cmdline",CancellationToken.None);
                if (cmdline.StartsWith(RemotePath+"\0",StringComparison.Ordinal)&&cmdline.Contains("--session="+session)) adb.RootShell("kill -TERM "+helperPid,CancellationToken.None);
            }
            catch (Exception) { /* The ADB channel closing normally already terminates the helper. */ }
            helperPid=0;
        }
    }
}
