using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ESPWorkbench
{
    public sealed class Instance
    {
        public int Index;
        public string Title;
        public IntPtr Window;
        public override string ToString() { return Index+" / "+Title; }
        public static List<Instance> Parse(string text)
        {
            List<Instance> list = new List<Instance>();
            foreach (string line in text.Split('\n'))
            {
                string[] parts = line.Trim().Split(','); int index; long main, child;
                if (parts.Length < 7 || !Int32.TryParse(parts[0],out index) || !Int64.TryParse(parts[2],out main) || !Int64.TryParse(parts[3],out child)) continue;
                if (parts[4] != "1") continue;
                IntPtr handle = new IntPtr(child == 0 ? main : child);
                if (Native.IsWindow(handle)) list.Add(new Instance { Index=index,Title=parts[1],Window=handle });
            }
            return list;
        }
    }
    public sealed class Settings
    {
        public string AdbPath = "", ConsolePath = "", ProfilePath = "", Serial = "";
        public RenderOptions Options=new RenderOptions();
        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json"); } }
        public static Settings Load() { try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); } catch { return new Settings(); } }
        public void Save() { File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this)); }
    }
    public sealed class MainForm : Form
    {
        private readonly TextBox adbPath = new TextBox(), consolePath = new TextBox(), profilePath = new TextBox(), log = new TextBox();
        private readonly ComboBox devices = new ComboBox(), instances = new ComboBox();
        private readonly Button scan = new Button(), inspect = new Button(), live = new Button(), demo = new Button(), stop = new Button();
        private readonly Label status = new Label();
        private readonly RenderOptions options = new RenderOptions();
        private readonly FrameBuffer frameBuffer=new FrameBuffer();
        private readonly ScenePanel scene;
        private readonly Overlay overlay;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch demoClock = new Stopwatch();
        private readonly Settings settings;
        private CancellationTokenSource readerCancel;
        private Task readerTask;
        private volatile Frame latest;
        private volatile bool liveDisplay;
        private bool demoMode, busy, closing, toggleHotkey, stopHotkey, menuHotkey,menuOpen;
        private static readonly Color Background = Color.FromArgb(17,24,35), Surface = Color.FromArgb(24,34,48);
        public MainForm(bool autoStart=false)
        {
            settings = Settings.Load();
            options=settings.Options??new RenderOptions(); options.EnsureDefaults();
            Text = "ESP Workbench | LDPlayer"; Size = new Size(1150,850); MinimumSize = new Size(1020,780);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Background; ForeColor = Color.Gainsboro; Font = new Font("Segoe UI",10);
            scene = new ScenePanel(options) { Dock = DockStyle.Fill, Margin = new Padding(12,0,0,0) };
            overlay = new Overlay(options);
            TableLayoutPanel layout = new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=5 };
            layout.BackColor=Background;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,390)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,150)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            Controls.Add(layout);
            Panel heading = new Panel { Dock=DockStyle.Fill };
            heading.Controls.Add(new Label { Text="ESP WORKBENCH",Font=new Font("Segoe UI",22,FontStyle.Bold),ForeColor=Painter.Accent,AutoSize=true,Location=new Point(0,0) });
            heading.Controls.Add(new Label { Text="LDPlayer connection / version-specific reader / overlay",AutoSize=true,Location=new Point(2,45),ForeColor=Color.Silver });
            layout.Controls.Add(heading,0,0); layout.SetColumnSpan(heading,2);
            TabControl tabs=new TabControl { Dock=DockStyle.Fill,Margin=new Padding(0,0,12,0) };
            TabPage connectionTab=new TabPage("الاتصال") { BackColor=Background,ForeColor=Color.Gainsboro }, displayTab=new TabPage("الملف والعرض") { BackColor=Background,ForeColor=Color.Gainsboro };
            tabs.TabPages.Add(connectionTab); tabs.TabPages.Add(displayTab);
            FlowLayoutPanel setup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            FlowLayoutPanel displaySetup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            connectionTab.Controls.Add(setup); displayTab.Controls.Add(displaySetup);
            layout.Controls.Add(tabs,0,1); layout.Controls.Add(scene,1,1);
            AddLabel(setup,"01  /  الاتصال بالمحاكي");
            ConfigurePath(setup,"ADB executable",adbPath,delegate { BrowseExecutable(adbPath,"adb.exe"); });
            ConfigurePath(setup,"LDPlayer console",consolePath,delegate { BrowseExecutable(consolePath,"ldconsole.exe"); });
            AddLabel(setup,"ADB device (actual serial)"); ConfigureCombo(setup,devices);
            AddLabel(setup,"Overlay target (select matching instance)"); ConfigureCombo(setup,instances);
            FlowLayoutPanel connectionButtons = Row(); ConfigureButton(scan,"تحديث الأجهزة",async delegate { await Scan(); }); ConfigureButton(inspect,"فحص اللعبة",async delegate { await Inspect(); }); connectionButtons.Controls.Add(scan); connectionButtons.Controls.Add(inspect); setup.Controls.Add(connectionButtons);
            AddLabel(displaySetup,"02  /  ملف بيانات الإصدار");
            ConfigurePath(displaySetup,"JSON profile",profilePath,delegate {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter="JSON profile (*.json)|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles") }) if (dialog.ShowDialog(this)==DialogResult.OK) profilePath.Text=dialog.FileName;
            });
            Label note = new Label { Text="ملف 4.6.0 مرتبط بالبناء المفحوص وبصمة المكتبة. Live يعرض البيانات المقروءة فعليًا؛ العرض التجريبي يستخدم بيانات مصطنعة.",Width=340,Height=72,ForeColor=Color.FromArgb(237,188,104),Margin=new Padding(0,12,0,4),RightToLeft=RightToLeft.Yes }; displaySetup.Controls.Add(note);
            AddLabel(displaySetup,"03  /  العرض");
            FlowLayoutPanel checks = new FlowLayoutPanel { Width=340,Height=90,Margin=Padding.Empty };
            AddCheck(checks,"Boxes",options.Boxes,v=>options.Boxes=v); AddCheck(checks,"Health",options.Health,v=>options.Health=v); AddCheck(checks,"Labels",options.Names,v=>options.Names=v); AddCheck(checks,"Distance",options.Distance,v=>options.Distance=v); AddCheck(checks,"Top lines",options.Lines,v=>options.Lines=v); AddCheck(checks,"Alerts",options.Alerts,v=>options.Alerts=v); AddCheck(checks,"Allies",options.Allies,v=>options.Allies=v); displaySetup.Controls.Add(checks);
            FlowLayoutPanel range = Row(); range.Controls.Add(new Label { Text="Max distance (m)",AutoSize=true,Margin=new Padding(0,7,8,0) });
            NumericUpDown distance = new NumericUpDown { Minimum=10,Maximum=5000,Value=(decimal)options.MaxDistance,Width=90,BackColor=Surface,ForeColor=Color.White }; distance.ValueChanged+=delegate { options.MaxDistance=(float)distance.Value; }; range.Controls.Add(distance); displaySetup.Controls.Add(range);
            FlowLayoutPanel runButtons = Row(); ConfigureButton(demo,"عرض تجريبي",delegate { StartDemo(); }); ConfigureButton(live,"تشغيل Live",async delegate { await StartLive(); }); ConfigureButton(stop,"إيقاف",async delegate { await StopReader(); }); stop.Width=70;
            Button menu=new Button(); ConfigureButton(menu,"القائمة الكاملة F7",delegate { OpenMenu(); }); menu.Width=165;
            runButtons.Width=1050; runButtons.Controls.Add(demo); runButtons.Controls.Add(live); runButtons.Controls.Add(stop); runButtons.Controls.Add(menu); runButtons.Controls.Add(new Label { Text="F8 hide/show   F9 stop",AutoSize=true,Margin=new Padding(12,7,0,0),ForeColor=Color.Silver }); layout.Controls.Add(runButtons,0,2); layout.SetColumnSpan(runButtons,2);
            AddLabel(setup,"التشخيص يقرأ بيانات الاتصال والذاكرة فقط.");
            AddLabel(setup,"Live يحتاج ملف إزاحات متحقق من هذا الإصدار.");
            log.Dock=DockStyle.Fill; log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.BackColor=Color.FromArgb(10,16,24); log.ForeColor=Color.Silver; log.BorderStyle=BorderStyle.FixedSingle; log.Font=new Font("Consolas",9); log.Margin=new Padding(0,12,0,0);
            layout.Controls.Add(log,0,3); layout.SetColumnSpan(log,2);
            status.Dock=DockStyle.Fill; status.TextAlign=ContentAlignment.MiddleLeft; status.ForeColor=Painter.Accent; layout.Controls.Add(status,0,4); layout.SetColumnSpan(status,2);
            string foundConsole = FindConsole();
            consolePath.Text = File.Exists(settings.ConsolePath) ? settings.ConsolePath : foundConsole;
            string bundledAdb = String.IsNullOrEmpty(consolePath.Text) ? "" : Path.Combine(Path.GetDirectoryName(consolePath.Text),"adb.exe");
            adbPath.Text = File.Exists(settings.AdbPath) ? settings.AdbPath : File.Exists(bundledAdb) ? bundledAdb : Adb.Find();
            string inspectedProfile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","pubg-global-4.6.0-21525.json");
            profilePath.Text = File.Exists(settings.ProfilePath)&&!settings.ProfilePath.EndsWith("pubg-global.template.json",StringComparison.OrdinalIgnoreCase) ? settings.ProfilePath : File.Exists(inspectedProfile)?inspectedProfile:Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","pubg-global.template.json");
            status.Text="Idle / waiting for connection";
            Native.timeBeginPeriod(1);
            timer.Interval=Math.Max(1,1000/options.RefreshRate); timer.Tick+=delegate {
                int requestedInterval=Math.Max(1,1000/Math.Max(30,options.RefreshRate)); if(timer.Interval!=requestedInterval) timer.Interval=requestedInterval;
                if (demoMode) latest=Demo.Make(demoClock.Elapsed.TotalSeconds);
                if (liveDisplay&&!demoMode&&Painter.Fresh(latest)) status.Text=String.Format("LIVE / {0} entities / {1:0} Hz / F7 menu / F8 hide / F9 stop",latest.Actors.Length,latest.ReadHertz);
                Frame displayed=demoMode?latest:frameBuffer.Sample();
                scene.Frame=demoMode||liveDisplay?displayed:null; scene.Invalidate(); overlay.UpdateFrame(demoMode||!liveDisplay ? null : displayed);
            }; timer.Start();
            Shown+=async delegate {
                toggleHotkey=Native.RegisterHotKey(Handle,1,0,0x77); stopHotkey=Native.RegisterHotKey(Handle,2,0,0x78); menuHotkey=Native.RegisterHotKey(Handle,3,0,0x76);
                if (!toggleHotkey || !stopHotkey) WriteLog("One or more global hotkeys are already in use; on-screen Stop remains available.");
                await Scan();
                if(autoStart&&devices.Items.Count==1&&instances.Items.Count==1) await StartLive();
            };
            FormClosing+=OnClosing;
        }
        private static string FindConsole()
        {
            foreach (Process p in Process.GetProcessesByName("dnplayer"))
            {
                try { string dir=Path.GetDirectoryName(p.MainModule.FileName); foreach (string n in new[] { "ldconsole.exe","dnconsole.exe" }) { string f=Path.Combine(dir,n); if (File.Exists(f)) return f; } } catch { } finally { p.Dispose(); }
            }
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(d=>d.DriveType==DriveType.Fixed))
                foreach (string folder in new[] { @"LDPlayer\LDPlayer14",@"LDPlayer\LDPlayer9",@"Program Files\LDPlayer\LDPlayer14" })
                    foreach (string name in new[] { "ldconsole.exe","dnconsole.exe" }) { string p=Path.Combine(drive.RootDirectory.FullName,folder,name); if (File.Exists(p)) return p; }
            return "";
        }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { Width=345,Height=40,Margin=new Padding(0,4,0,0),WrapContents=false }; }
        private static void AddLabel(Control parent,string text) { parent.Controls.Add(new Label { Text=text,AutoSize=true,Margin=new Padding(0,9,0,4),ForeColor=Color.Silver }); }
        private static void ConfigureCombo(Control parent,ComboBox combo) { combo.DropDownStyle=ComboBoxStyle.DropDownList; combo.Width=340; combo.BackColor=Surface; combo.ForeColor=Color.White; parent.Controls.Add(combo); }
        private static void ConfigureButton(Button b,string text,EventHandler handler) { b.Text=text; b.Width=126; b.Height=34; b.FlatStyle=FlatStyle.Flat; b.BackColor=Surface; b.ForeColor=Color.White; b.Margin=new Padding(0,0,5,0); b.Click+=handler; }
        private static void AddCheck(Control parent,string text,bool initial,Action<bool> changed) { CheckBox c=new CheckBox { Text=text,Checked=initial,AutoSize=true,Margin=new Padding(0,4,12,0) }; c.CheckedChanged+=delegate { changed(c.Checked); }; parent.Controls.Add(c); }
        private static void ConfigurePath(Control parent,string label,TextBox box,Action browse)
        {
            AddLabel(parent,label); FlowLayoutPanel row=Row(); row.Height=31; box.Width=297; box.BackColor=Surface; box.ForeColor=Color.White; box.BorderStyle=BorderStyle.FixedSingle;
            Button b=new Button { Text="...",Width=35,Height=27,FlatStyle=FlatStyle.Flat,Margin=new Padding(0) }; b.Click+=delegate { browse(); }; row.Controls.Add(box); row.Controls.Add(b); parent.Controls.Add(row);
        }
        private void BrowseExecutable(TextBox target,string name) { using (OpenFileDialog d=new OpenFileDialog { Filter="Executable (*.exe)|*.exe",FileName=name }) if (d.ShowDialog(this)==DialogResult.OK) target.Text=d.FileName; }
        private void WriteLog(string message)
        {
            if (IsDisposed || closing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(WriteLog),message); } catch (InvalidOperationException) { } return; }
            if (log.TextLength > 40000) log.Text=log.Text.Substring(log.TextLength-20000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);
        }
        private void SetBusy(bool value)
        {
            busy=value; bool running=readerTask!=null && !readerTask.IsCompleted;
            scan.Enabled=inspect.Enabled=!value&&!running; live.Enabled=demo.Enabled=!value&&!running;
            adbPath.Enabled=consolePath.Enabled=profilePath.Enabled=devices.Enabled=instances.Enabled=!value&&!running;
            stop.Enabled=!value && (demoMode||running||latest!=null);
        }
        private Adb SelectedAdb()
        {
            Device d=devices.SelectedItem as Device;
            if (d==null || d.State!="device") throw new IOException("Select a connected and authorized ADB device, then scan again if needed.");
            return new Adb(adbPath.Text,d.Serial);
        }
        public async Task Scan()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
            SetBusy(true); string adb=adbPath.Text, console=consolePath.Text;
            string previous=(devices.SelectedItem as Device)==null ? settings.Serial : ((Device)devices.SelectedItem).Serial;
            try
            {
                CommandResult result=await Task.Run(()=>Commands.Run(adb,new[] { "devices","-l" },15000,CancellationToken.None)); result.EnsureSuccess();
                List<Device> found=Adb.ParseDevices(result.Text); devices.Items.Clear(); devices.Items.AddRange(found.ToArray());
                Device preferred=found.FirstOrDefault(d=>d.Serial==previous) ?? found.FirstOrDefault(d=>d.State=="device") ?? found.FirstOrDefault(); if (preferred!=null) devices.SelectedItem=preferred;
                instances.Items.Clear();
                if (File.Exists(console))
                {
                    CommandResult listed=await Task.Run(()=>Commands.Run(console,new[] { "list2" },10000,CancellationToken.None)); listed.EnsureSuccess();
                    instances.Items.AddRange(Instance.Parse(listed.Text).ToArray()); if (instances.Items.Count>0) instances.SelectedIndex=0;
                }
                WriteLog("ADB: "+found.Count+" device(s); LDPlayer: "+instances.Items.Count+" running instance(s). Select the serial and its matching instance explicitly.");
                status.Text=found.Any(d=>d.State=="device") ? "Connected / run game inspection" : "No connected device / start LDPlayer and enable ADB";
                if (!String.IsNullOrWhiteSpace(result.Error)) WriteLog(result.Error);
            }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private async Task Inspect()
        {
            if (busy) return; SetBusy(true);
            try { Adb adb=SelectedAdb(); string text=await Task.Run(()=>Diagnostics.Run(adb)); WriteLog(text); status.Text="Inspection complete / see results below"; }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private void StartDemo()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
            demoMode=true; demoClock.Restart(); overlay.Hide(); latest=Demo.Make(0); status.Text="DEMO / generated actors only / no game data"; WriteLog("Demo started. All displayed positions and labels are synthetic."); SetBusy(false);
        }
        private async Task StartLive()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return; SetBusy(true);
            try
            {
                Profile p=Profile.Load(profilePath.Text); p.Validate(); Adb adb=SelectedAdb();
                Instance target=instances.SelectedItem as Instance; if (target==null || !Native.IsWindow(target.Window)) throw new IOException("Select the matching running LDPlayer window.");
                demoMode=false; latest=null; frameBuffer.Clear(); overlay.Hide();
                if (readerCancel!=null) readerCancel.Dispose();
                readerCancel=new CancellationTokenSource(); CancellationToken token=readerCancel.Token;
                GameInfo info=await Task.Run(()=>GameInfo.Inspect(adb,p.Package,true,token));
                if (p.VersionName!=info.VersionName || p.VersionCode!=info.VersionCode) throw new IOException("Profile/build mismatch. Installed: "+info.VersionName+" ("+info.VersionCode+"). Profile: "+p.VersionName+" ("+p.VersionCode+").");
                List<MapRegion> maps=MapRegion.Parse(info.Maps); ulong baseAddress=MapRegion.ModuleBase(maps,p.Module);
                AndroidMemory memory=new AndroidMemory(adb,info.Pid,maps,token);
                await Task.Run(()=>memory.Read(baseAddress,64));
                if ((info.Abi.Contains("64") && p.PointerSize!=8) || (!info.Abi.Contains("64") && info.Abi!="null" && !String.IsNullOrEmpty(info.Abi) && p.PointerSize!=4)) throw new IOException("Package ABI does not match the profile pointer width.");
                NativeReader nativeReader=null;
                if (p.Backend=="Native") { await Task.Run(()=>NativeReader.Prepare(adb,p,info,maps,token)); nativeReader=new NativeReader(adb,info.Pid,baseAddress); }
                GameReader reader=p.Backend=="Native"?null:new GameReader(p,memory,baseAddress); overlay.Target=target.Window; overlay.Suppressed=false;
                liveDisplay=true;
                readerTask=Task.Run(async delegate {
                    try
                    {
                        if (nativeReader!=null) await nativeReader.Stream(f=> {if(liveDisplay&&!token.IsCancellationRequested) {latest=f;frameBuffer.Push(f);}},token);
                        else while (!token.IsCancellationRequested)
                        {
                            latest=reader.Read(token);
                            frameBuffer.Push(latest);
                            await Task.Delay(p.PollMilliseconds,token);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { WriteLog("LIVE STOPPED: "+ex.Message); }
                    finally
                    {
                        liveDisplay=false; latest=null; frameBuffer.Clear();
                        if (!closing && !IsDisposed) try { BeginInvoke(new Action(delegate { overlay.Hide(); status.Text="Live reader stopped / see log"; SetBusy(false); })); } catch (InvalidOperationException) { }
                    }
                });
                WriteLog("Live reader started for "+p.Package+" "+info.VersionName+"; PID "+info.Pid+"; base 0x"+baseAddress.ToString("X")+". Overlay appears while the selected LDPlayer window is foreground."); status.Text="LIVE / press F9 to stop";
                Native.SetForegroundWindow(Native.GetAncestor(target.Window,2));
            }
            catch (Exception ex) { if (readerCancel!=null && (readerTask==null||readerTask.IsCompleted)) { readerCancel.Dispose(); readerCancel=null; } Failure(ex); }
            finally { SetBusy(false); }
        }
        public async Task StopReader()
        {
            liveDisplay=false; overlay.Suppressed=true; demoMode=false; latest=null; frameBuffer.Clear(); overlay.Hide();
            if (readerCancel!=null) readerCancel.Cancel();
            if (busy && readerTask==null) { status.Text="Cancelling startup..."; return; }
            if (readerTask!=null) { SetBusy(true); try { await readerTask; } catch (OperationCanceledException) { } readerTask=null; }
            if (readerCancel!=null) { readerCancel.Dispose(); readerCancel=null; }
            latest=null; frameBuffer.Clear();
            status.Text="Stopped"; WriteLog("Display and reader stopped."); SetBusy(false);
        }
        private void Failure(Exception ex) { status.Text="Action failed / see log"; WriteLog(ex.Message); }
        private void SaveOptions() { settings.Options=options; settings.Save(); }
        private void OpenMenu()
        {
            if(menuOpen||closing)return;menuOpen=true;
            try { using(OptionsForm form=new OptionsForm(options,SaveOptions)) form.ShowDialog(this); }
            catch(Exception ex) { Failure(ex); }
            finally { menuOpen=false; if(liveDisplay&&overlay.Target!=IntPtr.Zero) Native.SetForegroundWindow(Native.GetAncestor(overlay.Target,2)); }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg==0x0312)
            {
                if (m.WParam.ToInt32()==1) { overlay.Suppressed=!overlay.Suppressed; WriteLog(overlay.Suppressed?"Overlay hidden.":"Overlay enabled."); }
                else if (m.WParam.ToInt32()==2) { Task ignored=StopReader(); }
                else if (m.WParam.ToInt32()==3) OpenMenu();
            }
            base.WndProc(ref m);
        }
        private async void OnClosing(object sender,FormClosingEventArgs e)
        {
            if (closing) return;
            e.Cancel=true; closing=true; liveDisplay=false; overlay.Suppressed=true; overlay.Hide(); timer.Stop(); if (readerCancel!=null) readerCancel.Cancel();
            if (readerTask!=null) try { await readerTask; } catch { }
            if (toggleHotkey) Native.UnregisterHotKey(Handle,1); if (stopHotkey) Native.UnregisterHotKey(Handle,2); overlay.Close();
            if(menuHotkey)Native.UnregisterHotKey(Handle,3);
            Native.timeEndPeriod(1);
            if (readerCancel!=null) readerCancel.Dispose();
            settings.AdbPath=adbPath.Text; settings.ConsolePath=consolePath.Text; settings.ProfilePath=profilePath.Text; Device device=devices.SelectedItem as Device; settings.Serial=device==null?"":device.Serial;
            settings.Options=options;
            try { settings.Save(); } catch { }
            BeginInvoke(new Action(Close));
        }
    }
}
