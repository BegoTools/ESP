using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ESPWorkbench
{
    internal sealed class AndroidGameDisplay
    {
        public int Id,Width,Height;
        public static AndroidGameDisplay Parse(string text,string package)
        {
            Match focus=Regex.Match(text,@"FocusedDisplayId:\s*(\d+)");
            if(!focus.Success)throw new InvalidDataException("Android focused display is unavailable.");
            int id=Int32.Parse(focus.Groups[1].Value,CultureInfo.InvariantCulture);
            string windows=text.Contains("FocusedWindows:")?text.Substring(text.IndexOf("FocusedWindows:",StringComparison.Ordinal)):"";
            if(!Regex.IsMatch(windows,@"displayId="+id+@", name='[^'\r\n]*"+Regex.Escape(package)+@"/"))throw new InvalidDataException("The focused Android display is not showing the game. Return to PUBG before starting Live.");
            Match viewport=Regex.Match(text,@"Viewport [^\r\n]*displayId="+id+@",[^\r\n]*logicalFrame=\[0, 0, (\d+), (\d+)\]");
            int width,height;
            if(!viewport.Success||!Int32.TryParse(viewport.Groups[1].Value,out width)||!Int32.TryParse(viewport.Groups[2].Value,out height)||width<100||height<100||width>10000||height>10000)throw new InvalidDataException("Android game display bounds are unavailable.");
            return new AndroidGameDisplay { Id=id,Width=width,Height=height };
        }
    }
    // Shell-owned app_process uses the Android input service, independently of the game.
    // No process-memory writes, hooks, kernel drivers, or game-file changes.
    internal sealed class AndroidTouchInput : IAimInput,IDisposable
    {
        private readonly MouseInput keys=new MouseInput();
        private readonly object gate=new object();
        private readonly AutoResetEvent signal=new AutoResetEvent(false);
        private readonly Process process;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly Task worker,ackReader;
        private double pendingX,pendingY,lastMove,scaleX=1,scaleY=1;
        private bool paused=true;
        private volatile bool stopping;
        private volatile string failure;
        private long delivered,sequence,awaiting,skipped;
        public readonly AndroidGameDisplay Display;
        public string Description { get { return "Android Touch / display "+Display.Id+" / "+Display.Width+"x"+Display.Height; } }
        public long Delivered { get { return Interlocked.Read(ref delivered); } }
        public long Skipped { get { return Interlocked.Read(ref skipped); } }
        private AndroidTouchInput(Process process,AndroidGameDisplay display,Task<string> errors)
        {
            this.process=process;Display=display;
            ackReader=Task.Run(async delegate {
                try
                {
                    string line;
                    while((line=await process.StandardOutput.ReadLineAsync())!=null)
                    {
                        if(line.StartsWith("OK ",StringComparison.Ordinal)) { Interlocked.Increment(ref delivered);Interlocked.Exchange(ref awaiting,0); }
                        else if(line.StartsWith("SKIP ",StringComparison.Ordinal)) { Interlocked.Increment(ref skipped);Interlocked.Exchange(ref awaiting,0); }
                    }
                    if(!stopping) { string error=(await errors).Replace('\r',' ').Replace('\n',' ');failure="Android touch connection closed: "+error.Substring(0,Math.Min(180,error.Length)); }
                }
                catch(Exception ex) { if(!stopping)failure=ex.Message; }
            });
            worker=Task.Run((Action)Work);
        }
        public static AndroidTouchInput Start(Adb adb,string package,CancellationToken token)
        {
            AndroidGameDisplay display=AndroidGameDisplay.Parse(adb.Shell("dumpsys input",token),package);
            string payload=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native","esp-touch.jar");
            if(!File.Exists(payload))throw new FileNotFoundException("Android touch helper is missing.",payload);
            string hash;
            using(FileStream stream=File.OpenRead(payload))using(SHA256 sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
            string remote="/data/local/tmp/esp-workbench-touch-"+hash.Substring(0,16)+".jar";
            adb.Run(token,"push",payload,remote).EnsureSuccess();
            string remoteHash=adb.Shell("/system/bin/toybox sha256sum "+remote,token).Split(new[]{' ','\t','\r','\n'},StringSplitOptions.RemoveEmptyEntries)[0];
            if(!String.Equals(hash,remoteHash,StringComparison.OrdinalIgnoreCase))throw new IOException("Android touch helper transfer hash mismatch.");
            string session=Guid.NewGuid().ToString("N");
            string command="CLASSPATH="+remote+" app_process /system/bin EspTouchMain "+session+" "+display.Id+" "+display.Width+" "+display.Height;
            Process process=new Process { StartInfo=new ProcessStartInfo(adb.Executable,Commands.Quote("-s")+" "+Commands.Quote(adb.Serial)+" shell "+Commands.Quote(command)) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8 } };
            try
            {
                process.Start();Task<string> errors=process.StandardError.ReadToEndAsync();Task<string> ready=process.StandardOutput.ReadLineAsync();
                if(!ready.Wait(5000,token)||ready.Result!="READY 1 "+session)throw new IOException("Android touch helper did not start. "+(errors.IsCompleted?errors.Result:""));
                return new AndroidTouchInput(process,display,errors);
            }
            catch { try { if(!process.HasExited)process.Kill(); }catch { }process.Dispose();throw; }
        }
        public AimInputState Read(IntPtr target,AimOptions options)
        {
            AimInputState state=keys.Read(target,options);state.Error=failure;
            Rectangle viewport=Native.ClientBounds(target);
            if(viewport.Width>0&&viewport.Height>0)lock(gate) { scaleX=(double)Display.Width/viewport.Width;scaleY=(double)Display.Height/viewport.Height; }
            return state;
        }
        public bool Move(Point delta)
        {
            if(stopping||failure!=null)return false;
            lock(gate)
            {
                paused=false;lastMove=clock.Elapsed.TotalSeconds;
                // Bound backlog; only recent corrections can reach the device.
                pendingX=Math.Max(-40,Math.Min(40,pendingX+delta.X*scaleX));
                pendingY=Math.Max(-40,Math.Min(40,pendingY+delta.Y*scaleY));
            }
            signal.Set();return true;
        }
        public void Pause()
        {
            lock(gate) { paused=true;pendingX=pendingY=0; }
            signal.Set();
        }
        private void Work()
        {
            bool touching=false;double heartbeat=-1,lastCorrection=-1;
            try
            {
                while(!stopping)
                {
                    signal.WaitOne(8);
                    double dx,dy;bool release;
                    lock(gate)
                    {
                        release=paused||clock.Elapsed.TotalSeconds-lastMove>.08;
                        bool canSend=clock.Elapsed.TotalSeconds-lastCorrection>=.016&&Interlocked.Read(ref awaiting)==0;
                        dx=release||!canSend?0:pendingX;dy=release||!canSend?0:pendingY;
                        if(release||canSend)pendingX=pendingY=0;
                    }
                    if(release&&touching) { Write("U "+(++sequence));touching=false; }
                    else if(dx!=0||dy!=0)
                    {
                        long next=++sequence;Interlocked.Exchange(ref awaiting,next);
                        Write("M "+next+" "+dx.ToString("0.000",CultureInfo.InvariantCulture)+" "+dy.ToString("0.000",CultureInfo.InvariantCulture));touching=true;lastCorrection=clock.Elapsed.TotalSeconds;
                    }
                    if(clock.Elapsed.TotalSeconds-heartbeat>=.1) { Write("PING");heartbeat=clock.Elapsed.TotalSeconds; }
                }
                Write("U "+(++sequence));
            }
            catch(Exception ex) { if(!stopping)failure=ex.Message; }
        }
        private void Write(string line) { process.StandardInput.WriteLine(line);process.StandardInput.Flush(); }
        public void Dispose()
        {
            if(stopping)return;stopping=true;Pause();
            try { worker.Wait(300);process.StandardInput.Close();if(!process.WaitForExit(500))process.Kill(); }
            catch(InvalidOperationException) { }catch(IOException) { }
            try { ackReader.Wait(300); }catch(AggregateException) { }
            // EOF releases the touch; independent device watchdog also releases on idle.
            process.Dispose();
            if(worker.IsCompleted)signal.Dispose();
        }
    }
}
