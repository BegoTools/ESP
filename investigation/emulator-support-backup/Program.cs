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
            CancellationToken token=CancellationToken.None;
            GameInfo info=GameInfo.Inspect(adb,"com.tencent.ig",false,token);
            string report="Package: com.tencent.ig\r\nVersion: "+info.VersionName+" / code "+info.VersionCode+"\r\nPID: "+info.Pid+"\r\nABI: "+info.Abi+"\r\nAndroid: "+info.AndroidVersion+"\r\nRoot: "+info.Root;
            if (!info.Root) return report+"\r\nMemory test unavailable without existing root permission.";
            try
            {
                List<MapRegion> maps=MapRegion.Parse(adb.RootShell("cat /proc/"+info.Pid+"/maps",token));
                ulong module=MapRegion.ModuleBase(maps,"libUE4.so");
                AndroidMemory memory=new AndroidMemory(adb,info.Pid,maps,token);
                byte[] page=memory.Read(module,4096);
                report+="\r\nlibUE4.so base: 0x"+module.ToString("X")+"\r\nMemory transport: PASS / "+page.Length+" decoded bytes";
                report+="\r\nUse the inspected 4.6.0 (21525) native profile for live data; startup also verifies library SHA-256.";
            }
            catch (Exception ex) { report+="\r\nMemory transport: FAILED / "+ex.Message; }
            return report;
        }
    }
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Native.SetProcessDPIAware();
            try
            {
                if (args.Length>=2 && args[0]=="--self-test") { File.WriteAllText(args[1],SelfTests.Run()); return 0; }
                if(args.Length>=2&&args[0]=="--render-menu") { using(OptionsForm menu=new OptionsForm(new RenderOptions(),delegate {}))menu.RenderPages(args[1]);return 0; }
                if (args.Length>=2 && args[0]=="--render-demo")
                {
                    using (Bitmap bitmap=new Bitmap(1000,620)) using (Graphics g=Graphics.FromImage(bitmap))
                    { Painter.Draw(g,bitmap.Size,Demo.Make(0),new RenderOptions { Allies=true,Lines=true },true); bitmap.Save(args[1],ImageFormat.Png); } return 0;
                }
                if (args.Length>=4 && args[0]=="--diagnose") { File.WriteAllText(args[3],Diagnostics.Run(new Adb(args[1],args[2]))); return 0; }
                if (args.Length>=4 && args[0]=="--capture-native")
                {
                    Adb adb=new Adb(args[1],args[2]); CancellationToken token=CancellationToken.None;
                    Profile profile=Profile.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","pubg-global-4.6.0-21525.json")); profile.Validate();
                    GameInfo info=GameInfo.Inspect(adb,profile.Package,true,token);
                    if (info.VersionName!=profile.VersionName || info.VersionCode!=profile.VersionCode) throw new InvalidDataException("Game/profile version mismatch.");
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
                    using (MainForm form=new MainForm())
                    {
                        form.Show(); Application.DoEvents();
                        using (Bitmap image=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(image,new Rectangle(Point.Empty,form.Size)); image.Save(args[1],ImageFormat.Png); }
                        form.Close(); Application.DoEvents();
                    } return 0;
                }
                Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show(e.Exception.Message,"ESP Workbench",MessageBoxButtons.OK,MessageBoxIcon.Error); };
                Application.Run(new MainForm(args.Contains("--live"))); return 0;
            }
            catch (Exception ex)
            {
                if (args.Length>=2 && args[0]=="--self-test") File.WriteAllText(args[1],"FAILED\r\n"+ex.ToString());
                else File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"last-error.txt"),ex.ToString());
                return 1;
            }
        }
    }
}
