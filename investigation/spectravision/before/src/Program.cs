using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ESPWorkbench
{
    public static class Diagnostics
    {
        public static string Run(Adb adb)
        {
            return Run(adb,Profile.ForInstalled(GameInfo.Inspect(adb,"com.tencent.ig",false,CancellationToken.None)));
        }
        public static string Run(Adb adb,Profile profile)
        {
            CancellationToken token=CancellationToken.None;
            profile.Validate();
            GameInfo info=GameInfo.Inspect(adb,profile.Package,false,token);
            string report="ADB: "+adb.Executable+"\r\nSerial: "+adb.Serial+"\r\nPackage: "+profile.Package+"\r\nVersion: "+info.VersionName+" / code "+info.VersionCode+"\r\nPID: "+info.Pid+"\r\nABI: "+info.Abi+"\r\nAndroid: "+info.AndroidVersion+"\r\nRoot: "+info.Root+" / "+adb.Privilege;
            List<string> problems=GameCompatibility.Problems(profile,info);
            report+="\r\nProfile: "+profile.Name+"\r\nLive prerequisites: "+(problems.Count==0?"PASS (library hash and memory still require verification)":"FAILED\r\n"+String.Join("\r\n",problems));
            if (!info.Root) return report+"\r\nMemory test unavailable without existing root permission.";
            try
            {
                List<MapRegion> maps=MapRegion.Parse(adb.ReadMaps(info.Pid,token));
                ulong module=MapRegion.ModuleBase(maps,"libUE4.so");
                AndroidMemory memory=new AndroidMemory(adb,info.Pid,maps,token);
                byte[] page=memory.Read(module,4096);
                report+="\r\nlibUE4.so base: 0x"+module.ToString("X")+"\r\nMemory transport: PASS / "+page.Length+" decoded bytes";
                report+="\r\nLive startup also verifies the profile's library SHA-256.";
            }
            catch (Exception ex) { report+="\r\nMemory transport: FAILED / "+ex.Message; }
            return report;
        }
    }
    internal static class Program
    {
        private static Mutex appMutex;
        private static bool ownsMutex;
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Native.SetProcessDPIAware();
            try
            {
                if (args.Length>=2 && args[0]=="--emulator-windows") { File.WriteAllText(args[1],Emulators.WindowReport()); return 0; }
                if (args.Length>=2 && args[0]=="--self-test") { File.WriteAllText(args[1],SelfTests.Run()); return 0; }
                if(args.Length>=2&&args[0]=="--render-menu") { using(OptionsForm menu=new OptionsForm(new RenderOptions(),delegate {}))menu.RenderPages(args[1]);return 0; }
                if(args.Length>=2&&args[0]=="--render-quick")
                {
                    Directory.CreateDirectory(args[1]);RenderOptions options=new RenderOptions();options.Aim.Enabled=true;options.Aim.Strength=35;options.Aim.Radius=151;options.Aim.InputScale=.94f;options.Aim.Activation="Firing";
                    for(int i=0;i<2;i++)using(Bitmap bitmap=new Bitmap(1000,620))using(Graphics graphics=Graphics.FromImage(bitmap))
                    {
                        Painter.Draw(graphics,bitmap.Size,Demo.Make(0),options,true,false);
                        QuickHud.Draw(graphics,bitmap.Size,options,new QuickHudState { Open=i==1,SelectedRow=1 },"اضرب لتفعيل المساعدة");
                        bitmap.Save(Path.Combine(args[1],i==0?"collapsed.png":"expanded.png"),ImageFormat.Png);
                    }
                    return 0;
                }
                if (args.Length>=2 && args[0]=="--render-demo")
                {
                    using (Bitmap bitmap=new Bitmap(1000,620)) using (Graphics g=Graphics.FromImage(bitmap))
                    { Painter.Draw(g,bitmap.Size,Demo.Make(0),new RenderOptions { Allies=true,Lines=true },true); bitmap.Save(args[1],ImageFormat.Png); } return 0;
                }
                if (args.Length>=4 && args[0]=="--diagnose") { File.WriteAllText(args[3],Diagnostics.Run(new Adb(args[1],args[2]))); return 0; }
                if (args.Length>=4 && args[0]=="--capture-native")
                {
                    Adb adb=new Adb(args[1],args[2]); CancellationToken token=CancellationToken.None;
                    GameInfo info=GameInfo.Inspect(adb,"com.tencent.ig",false,token);
                    Profile profile=args.Length>=5?Profile.Load(args[4]):Profile.ForInstalled(info); profile.Validate();
                    GameCompatibility.Ensure(profile,info,"Selected emulator");
                    info.Maps=adb.ReadMaps(info.Pid,token);
                    System.Collections.Generic.List<MapRegion> maps=MapRegion.Parse(info.Maps);
                    NativeReader.Prepare(adb,profile,info,maps,token);
                    NativeSnapshot snapshot=new NativeReader(adb,info.Pid,MapRegion.ModuleBase(maps,profile.Module)).Once(token);
                    File.WriteAllText(args[3],new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(snapshot)); return 0;
                }
                if (args.Length>=4 && args[0]=="--render-snapshot")
                {
                    Frame frame=NativeSnapshot.Parse(File.ReadAllText(args[1])).ToFrame();
                    using (Bitmap bitmap=new Bitmap(args[2])) using (Graphics g=Graphics.FromImage(bitmap))
                    { Painter.Draw(g,bitmap.Size,frame,new RenderOptions { Allies=true,Lines=true },false); bitmap.Save(args[3],ImageFormat.Png); } return 0;
                }
                if (args.Length>=2 && args[0]=="--smoke-ui")
                {
                    EmulatorKind? selected=args.Length>=3?(EmulatorKind?)Enum.Parse(typeof(EmulatorKind),args[2],true):null;
                    using (MainForm form=new MainForm(false,selected))
                    {
                        form.Show(); Application.DoEvents();
                        System.Diagnostics.Stopwatch wait=System.Diagnostics.Stopwatch.StartNew();
                        while(form.InitialScan!=null&&!form.InitialScan.IsCompleted&&wait.ElapsedMilliseconds<30000) { Application.DoEvents(); Thread.Sleep(10); }
                        if(form.InitialScan!=null&&!form.InitialScan.IsCompleted) throw new TimeoutException("Initial UI scan did not complete.");
                        if(args.Contains("--probe-live"))
                        {
                            form.Activate();
                            var probe=form.StartLive(); wait.Restart();
                            while(!probe.IsCompleted&&wait.ElapsedMilliseconds<30000) { Application.DoEvents(); Thread.Sleep(10); }
                            if(!probe.IsCompleted) throw new TimeoutException("Live startup probe did not complete.");
                            probe.GetAwaiter().GetResult();
                            wait.Restart();
                            while((!form.HasLiveFrame||wait.ElapsedMilliseconds<500)&&wait.ElapsedMilliseconds<5000) {Application.DoEvents();Thread.Sleep(10);}
                            if(!form.HasLiveFrame)throw new IOException("Live UI probe did not receive a fresh game frame.");
                            if(args.Contains("--verify-overlay"))
                            {
                                wait.Restart();
                                while(!form.HasVisibleOverlay&&wait.ElapsedMilliseconds<20000) {Application.DoEvents();Thread.Sleep(10);}
                                if(!form.HasVisibleOverlay)throw new IOException("Overlay was not visible; restore and foreground the game window during this probe.");
                            }
                        }
                        if(args.Contains("--observe-aim")||args.Contains("--observe-quick"))
                        {
                            int seconds=45,secondsIndex=Array.IndexOf(args,"--observe-aim-seconds");
                            if(secondsIndex>=0&&(secondsIndex+1>=args.Length||!Int32.TryParse(args[secondsIndex+1],out seconds)||seconds<5||seconds>60))throw new ArgumentException("Aim observation duration must be 5-60 seconds.");
                            var samples=new System.Collections.Generic.List<object>();wait.Restart();long lastSample=-1;
                            while(wait.ElapsedMilliseconds<seconds*1000)
                            {
                                Application.DoEvents();Thread.Sleep(5);
                                if(wait.ElapsedMilliseconds-lastSample>=100) { samples.Add(new { Milliseconds=wait.ElapsedMilliseconds,Aim=form.AimStatus(),Quick=form.QuickStatus() });lastSample=wait.ElapsedMilliseconds; }
                            }
                            File.WriteAllText(args[1]+".aim.json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(samples));
                        }
                        File.WriteAllText(args[1]+".json",new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(form.RuntimeStatus()));
                        using (Bitmap image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(args[1],ImageFormat.Png); }
                        form.Close(); wait.Restart();
                        while(!form.IsDisposed&&wait.ElapsedMilliseconds<10000) {Application.DoEvents();Thread.Sleep(10);}
                        if(!form.IsDisposed)throw new TimeoutException("UI reader shutdown did not finish.");
                    } return 0;
                }
                Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message,"ESP Workbench",MessageBoxButtons.OK,MessageBoxIcon.Error); };
                int emulatorIndex=Array.IndexOf(args,"--emulator"); EmulatorKind? preferred=null;
                if(emulatorIndex>=0)
                {
                    if(emulatorIndex+1>=args.Length) throw new ArgumentException("--emulator requires LDPlayer or GameLoop.");
                    preferred=(EmulatorKind)Enum.Parse(typeof(EmulatorKind),args[emulatorIndex+1],true);
                    if(!Enum.IsDefined(typeof(EmulatorKind),preferred.Value)) throw new ArgumentException("Unknown emulator.");
                }
                try
                {
                    appMutex=new Mutex(false,"Local\\ESPWorkbench.UserInstance");
                    try { ownsMutex=appMutex.WaitOne(args.Contains("--restart-elevated")?10000:0); }catch(AbandonedMutexException) { ownsMutex=true; }
                }
                catch(UnauthorizedAccessException) { ownsMutex=false; }
                if(!ownsMutex)
                {
                    Native.EnumWindows(delegate(IntPtr window,IntPtr unused) { if(Native.WindowText(window).StartsWith("ESP Workbench |",StringComparison.Ordinal)) { Native.ShowWindowAsync(window,9);Native.SetForegroundWindow(window);return false; }return true; },IntPtr.Zero);
                    return 0;
                }
                Application.Run(new MainForm(args.Contains("--live"),preferred)); return 0;
            }
            catch (Exception ex)
            {
                if (args.Length>=2 && args[0]=="--self-test") File.WriteAllText(args[1],"FAILED\r\n"+ex.ToString());
                else File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-error.txt"),ex.ToString());
                return 1;
            }
            finally { if(ownsMutex&&appMutex!=null)appMutex.ReleaseMutex();if(appMutex!=null)appMutex.Dispose(); }
        }
    }
}
