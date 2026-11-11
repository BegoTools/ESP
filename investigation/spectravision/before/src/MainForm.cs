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
        public IntPtr HostWindow;
        public EmulatorKind Emulator;
        public override string ToString() { return Index+" / "+Title+(Emulator==EmulatorKind.GameLoop&&Window==IntPtr.Zero?" [render surface unavailable]":""); }
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
        public string Emulator = "LDPlayer";
        public EmulatorConnection LDPlayer, GameLoop;
        public RenderOptions Options=new RenderOptions();
        public EmulatorConnection Connection(EmulatorKind kind)
        {
            if (LDPlayer==null) LDPlayer=new EmulatorConnection { AdbPath=AdbPath,ConsolePath=ConsolePath,Serial=Serial,ProfilePath=ProfilePath };
            else if(String.IsNullOrEmpty(LDPlayer.ProfilePath)) LDPlayer.ProfilePath=ProfilePath;
            if (GameLoop==null) GameLoop=new EmulatorConnection();
            return kind==EmulatorKind.GameLoop?GameLoop:LDPlayer;
        }
        public static string FilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json"); } }
        public static Settings Load() { try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); } catch { return new Settings(); } }
        public void Save() { File.WriteAllText(FilePath,new JavaScriptSerializer().Serialize(this)); }
    }
    public sealed class MainForm : Form
    {
        private readonly TextBox adbPath = new TextBox(), consolePath = new TextBox(), endpoint = new TextBox(), profilePath = new TextBox(), log = new TextBox();
        private readonly ComboBox emulators = new ComboBox(), devices = new ComboBox(), instances = new ComboBox();
        private Control consoleRow, endpointRow, connectionSetup;
        private EmulatorKind activeEmulator;
        private readonly Button scan = new Button(), inspect = new Button(), live = new Button(), demo = new Button(), stop = new Button();
        private readonly Label status = new Label();
        private readonly RenderOptions options = new RenderOptions();
        private readonly FrameBuffer frameBuffer=new FrameBuffer();
        private readonly AimController aim=new AimController();
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
        private bool fullMenuHotkey,quickKeysActive,quickArrowsActive,quickSavePending;
        private DateTime quickSaveDue;
        private readonly HashSet<int> quickRegistered=new HashSet<int>();
        public Task InitialScan { get; private set; }
        internal bool HasLiveFrame { get { return liveDisplay&&Painter.Fresh(latest); } }
        internal bool HasVisibleOverlay { get { return HasLiveFrame&&overlay.Visible; } }
        internal object AimStatus()
        {
            AimTarget target=aim.Target;
            Frame decision=aim.DecisionFrame;
            return new { Enabled=options.Aim.Enabled,Strength=options.Aim.Strength,Radius=options.Aim.Radius,AimPoint=options.Aim.AimPoint,Activation=options.Aim.Activation,Backend=aim.InputDescription,ToolIntegrity=WindowsInputAccess.CurrentIntegrity,LocalScoped=decision==null?(bool?)null:decision.LocalScoped,LocalFiring=decision==null?(bool?)null:decision.LocalFiring,State=aim.State,SentMovements=aim.SentMovements,DeliveredMovements=aim.DeliveredMovements,Input=aim.InputState,Target=target==null?null:target.Address,Candidate=aim.Candidate==null?null:aim.Candidate.Address,Error=target==null?(double?)null:target.Error,CameraRotation=decision==null?(Vec3?)null:decision.CameraRotation };
        }
        internal object RuntimeStatus()
        {
            IntPtr target=overlay.Target;
            return new { Emulator=Emulators.Name(activeEmulator),SelectedEmulator=emulators.Text,Profile=profilePath.Text,Serial=(devices.SelectedItem as Device)==null?"":((Device)devices.SelectedItem).Serial,Live=liveDisplay,FreshFrame=Painter.Fresh(latest),Entities=latest==null?0:latest.Actors.Length,Hertz=latest==null?0:latest.ReadHertz,RenderWindow=target.ToInt64(),RenderBounds=Native.ClientBounds(target),OverlayBounds=overlay.Bounds,OverlayVisible=overlay.Visible,Foreground=Native.GetForegroundWindow().ToInt64(),TargetRoot=Native.GetAncestor(target,2).ToInt64() };
        }
        internal object QuickStatus()
        {
            return new { Open=overlay.Quick.Open,Row=overlay.Quick.SelectedRow,Aim=options.Aim.Enabled,Mode=options.Aim.Activation,Point=options.Aim.AimPoint,Strength=options.Aim.Strength,Radius=options.Aim.Radius,Scale=options.Aim.InputScale,Esp=options.EspEnabled,GameFocused=GameFocused(),Registered=quickRegistered.OrderBy(i=>i).ToArray(),Unavailable=overlay.Quick.Unavailable.OrderBy(i=>i).ToArray(),SavePending=quickSavePending };
        }
        private static readonly Color Background = Color.FromArgb(17,24,35), Surface = Color.FromArgb(24,34,48);
        public MainForm(bool autoStart=false, EmulatorKind? selectedEmulator=null)
        {
            settings = Settings.Load();
            options=settings.Options??new RenderOptions(); options.EnsureDefaults();
            Text = "ESP Workbench | LDPlayer / GameLoop"; Size = new Size(1150,920); MinimumSize = new Size(1020,850);
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
            heading.Controls.Add(new Label { Text="LDPlayer / GameLoop connection / version-specific reader / overlay",AutoSize=true,Location=new Point(2,45),ForeColor=Color.Silver });
            layout.Controls.Add(heading,0,0); layout.SetColumnSpan(heading,2);
            TabControl tabs=new TabControl { Dock=DockStyle.Fill,Margin=new Padding(0,0,12,0) };
            TabPage connectionTab=new TabPage("الاتصال") { BackColor=Background,ForeColor=Color.Gainsboro }, displayTab=new TabPage("الملف والعرض") { BackColor=Background,ForeColor=Color.Gainsboro };
            tabs.TabPages.Add(connectionTab); tabs.TabPages.Add(displayTab);
            FlowLayoutPanel setup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            FlowLayoutPanel displaySetup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            connectionTab.Controls.Add(setup); displayTab.Controls.Add(displaySetup);
            connectionSetup=setup;
            layout.Controls.Add(tabs,0,1); layout.Controls.Add(scene,1,1);
            AddLabel(setup,"01  /  الاتصال بالمحاكي");
            AddLabel(setup,"المحاكي / Emulator"); ConfigureCombo(setup,emulators); emulators.Items.AddRange(new object[] { "LDPlayer 14", "GameLoop" });
            ConfigurePath(setup,"ADB executable",adbPath,delegate { BrowseExecutable(adbPath,"adb.exe"); });
            consoleRow=ConfigurePath(setup,"LDPlayer console",consolePath,delegate { BrowseExecutable(consolePath,"ldconsole.exe"); });
            endpointRow=ConfigurePath(setup,"GameLoop ADB address",endpoint,null);
            AddLabel(setup,"ADB device (actual serial)"); ConfigureCombo(setup,devices);
            AddLabel(setup,"Overlay target (select matching instance)"); ConfigureCombo(setup,instances);
            FlowLayoutPanel connectionButtons = Row(); ConfigureButton(scan,"تحديث الأجهزة",async delegate { await Scan(); }); ConfigureButton(inspect,"فحص اللعبة",async delegate { await Inspect(); }); connectionButtons.Controls.Add(scan); connectionButtons.Controls.Add(inspect); setup.Controls.Add(connectionButtons);
            AddLabel(displaySetup,"02  /  ملف بيانات الإصدار");
            ConfigurePath(displaySetup,"JSON profile",profilePath,delegate {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter="JSON profile (*.json)|*.json",InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles") }) if (dialog.ShowDialog(this)==DialogResult.OK) profilePath.Text=dialog.FileName;
            });
            Label note = new Label { Text="ملفا 4.6.0 للبناءين 21525 و21518 مرتبطان ببصمة المكتبة. Live يقرأ بيانات فعلية؛ العرض التجريبي يستخدم بيانات مصطنعة.",Width=340,Height=72,ForeColor=Color.FromArgb(237,188,104),Margin=new Padding(0,12,0,4),RightToLeft=RightToLeft.Yes }; displaySetup.Controls.Add(note);
            AddLabel(displaySetup,"03  /  العرض");
            FlowLayoutPanel checks = new FlowLayoutPanel { Width=340,Height=90,Margin=Padding.Empty };
            AddCheck(checks,"Boxes",options.Boxes,v=>options.Boxes=v); AddCheck(checks,"Health",options.Health,v=>options.Health=v); AddCheck(checks,"Labels",options.Names,v=>options.Names=v); AddCheck(checks,"Distance",options.Distance,v=>options.Distance=v); AddCheck(checks,"Top lines",options.Lines,v=>options.Lines=v); AddCheck(checks,"Alerts",options.Alerts,v=>options.Alerts=v); AddCheck(checks,"Allies",options.Allies,v=>options.Allies=v); displaySetup.Controls.Add(checks);
            FlowLayoutPanel range = Row(); range.Controls.Add(new Label { Text="Max distance (m)",AutoSize=true,Margin=new Padding(0,7,8,0) });
            NumericUpDown distance = new NumericUpDown { Minimum=10,Maximum=5000,Value=(decimal)options.MaxDistance,Width=90,BackColor=Surface,ForeColor=Color.White }; distance.ValueChanged+=delegate { options.MaxDistance=(float)distance.Value; }; range.Controls.Add(distance); displaySetup.Controls.Add(range);
            FlowLayoutPanel runButtons = Row(); ConfigureButton(demo,"عرض تجريبي",delegate { StartDemo(); }); ConfigureButton(live,"تشغيل Live",async delegate { await StartLive(); }); ConfigureButton(stop,"إيقاف",async delegate { await StopReader(); }); stop.Width=70;
            Button menu=new Button(); ConfigureButton(menu,"القائمة الكاملة Ctrl+F7",delegate { OpenMenu(); }); menu.Width=190;
            Button administrator=new Button();ConfigureButton(administrator,"تشغيل كمسؤول",delegate { RestartAsAdministrator(); });administrator.Width=145;
            runButtons.Width=1050; runButtons.Controls.Add(demo); runButtons.Controls.Add(live); runButtons.Controls.Add(stop); runButtons.Controls.Add(menu);runButtons.Controls.Add(administrator); runButtons.Controls.Add(new Label { Text="F8 hide/show   F9 stop",AutoSize=true,Margin=new Padding(12,7,0,0),ForeColor=Color.Silver }); layout.Controls.Add(runButtons,0,2); layout.SetColumnSpan(runButtons,2);
            AddLabel(setup,"التشخيص يقرأ بيانات الاتصال والذاكرة فقط.");
            AddLabel(setup,"Live يحتاج ملف إزاحات متحقق من هذا الإصدار.");
            log.Dock=DockStyle.Fill; log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.BackColor=Color.FromArgb(10,16,24); log.ForeColor=Color.Silver; log.BorderStyle=BorderStyle.FixedSingle; log.Font=new Font("Consolas",9); log.Margin=new Padding(0,12,0,0);
            layout.Controls.Add(log,0,3); layout.SetColumnSpan(log,2);
            status.Dock=DockStyle.Fill; status.TextAlign=ContentAlignment.MiddleLeft; status.ForeColor=Painter.Accent; layout.Controls.Add(status,0,4); layout.SetColumnSpan(status,2);
            activeEmulator=selectedEmulator??(settings.Emulator=="GameLoop"?EmulatorKind.GameLoop:EmulatorKind.LDPlayer);
            emulators.SelectedIndex=activeEmulator==EmulatorKind.GameLoop?1:0;
            LoadConnection();
            emulators.SelectedIndexChanged+=async delegate {
                SaveConnection(); activeEmulator=emulators.SelectedIndex==1?EmulatorKind.GameLoop:EmulatorKind.LDPlayer;
                demoMode=false; liveDisplay=false; latest=null; frameBuffer.Clear(); overlay.Hide();
                devices.Items.Clear(); instances.Items.Clear(); LoadConnection();
                settings.Emulator=activeEmulator.ToString(); SaveOptions(); await Scan();
            };
            status.Text="Idle / waiting for connection";
            Native.timeBeginPeriod(1);
            timer.Interval=Math.Max(1,1000/options.RefreshRate); timer.Tick+=delegate {
                int requestedInterval=Math.Max(1,1000/Math.Max(30,options.RefreshRate)); if(timer.Interval!=requestedInterval) timer.Interval=requestedInterval;
                if (demoMode) latest=Demo.Make(demoClock.Elapsed.TotalSeconds);
                if (liveDisplay&&!demoMode&&Painter.Fresh(latest)) status.Text=String.Format("LIVE / {0} entities / {1:0} Hz / F3-F7 quick / Ctrl+F7 menu / F9 stop",latest.Actors.Length,latest.ReadHertz);
                UpdateQuickBindings();
                if(quickSavePending&&DateTime.UtcNow>=quickSaveDue)
                {
                    quickSavePending=false;try { SaveOptions(); }catch(Exception ex) { WriteLog("Quick settings could not be saved: "+ex.Message); }
                }
                Frame displayed=demoMode?latest:frameBuffer.Sample();
                scene.Frame=demoMode||liveDisplay?displayed:null; scene.Invalidate(); overlay.UpdateFrame(demoMode||!liveDisplay ? null : displayed);
                if(!aim.Update(latest,overlay.Target,options.Aim,liveDisplay&&!demoMode&&!menuOpen&&!closing&&!overlay.Suppressed)) WriteLog("Aim input transport failed. Aim disabled; restart Live and check the selected input method.");
                overlay.AimState=aim.State;
                overlay.AimCandidate=aim.Candidate;
                overlay.AimMovements=aim.DeliveredMovements;
            }; timer.Start();
            Shown+=async delegate {
                toggleHotkey=Native.RegisterHotKey(Handle,1,0x4000,0x77); stopHotkey=Native.RegisterHotKey(Handle,2,0x4000,0x78); menuHotkey=Native.RegisterHotKey(Handle,3,0x4000,0x76);
                if(!menuHotkey)overlay.Quick.Unavailable.Add(7);
                if (!toggleHotkey || !stopHotkey||!menuHotkey) WriteLog("One or more hotkeys are already in use; on-screen menu and Stop remain available.");
                InitialScan=Scan(); await InitialScan;
                if(autoStart&&devices.Items.Count==1&&instances.Items.Count==1) await StartLive();
            };
            FormClosing+=OnClosing;
        }
        private void LoadConnection()
        {
            EmulatorConnection connection=settings.Connection(activeEmulator);
            consolePath.Text=File.Exists(connection.ConsolePath)?connection.ConsolePath:activeEmulator==EmulatorKind.LDPlayer?Emulators.FindConsole():"";
            adbPath.Text=File.Exists(connection.AdbPath)?connection.AdbPath:Emulators.FindAdb(activeEmulator,consolePath.Text);
            endpoint.Text=connection.Endpoint;
            string defaultProfile=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles",activeEmulator==EmulatorKind.GameLoop?"pubg-global-4.6.0-21518.json":"pubg-global-4.6.0-21525.json");
            profilePath.Text=File.Exists(connection.ProfilePath)&&!connection.ProfilePath.EndsWith("pubg-global.template.json",StringComparison.OrdinalIgnoreCase)?connection.ProfilePath:defaultProfile;
            consoleRow.Visible=((Control)consoleRow.Tag).Visible=activeEmulator==EmulatorKind.LDPlayer;
            endpointRow.Visible=((Control)endpointRow.Tag).Visible=activeEmulator==EmulatorKind.GameLoop;
        }
        private void SaveConnection()
        {
            EmulatorConnection connection=settings.Connection(activeEmulator);
            connection.AdbPath=adbPath.Text; connection.ConsolePath=consolePath.Text; connection.Endpoint=endpoint.Text;
            connection.ProfilePath=profilePath.Text;
            Device device=devices.SelectedItem as Device; if(device!=null) connection.Serial=device.Serial;
            settings.Emulator=activeEmulator.ToString();
            // Preserve the original LDPlayer fields for existing settings and older builds.
            settings.AdbPath=settings.LDPlayer.AdbPath; settings.ConsolePath=settings.LDPlayer.ConsolePath; settings.Serial=settings.LDPlayer.Serial;settings.ProfilePath=settings.LDPlayer.ProfilePath;
        }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { Width=345,Height=40,Margin=new Padding(0,4,0,0),WrapContents=false }; }
        private static void AddLabel(Control parent,string text) { parent.Controls.Add(new Label { Text=text,AutoSize=true,Margin=new Padding(0,9,0,4),ForeColor=Color.Silver }); }
        private static void ConfigureCombo(Control parent,ComboBox combo)
        {
            combo.DropDownStyle=ComboBoxStyle.DropDownList; combo.Width=340; combo.BackColor=Surface; combo.ForeColor=Color.White;
            combo.FlatStyle=FlatStyle.Flat; combo.DrawMode=DrawMode.OwnerDrawFixed; combo.ItemHeight=22;
            combo.DrawItem+=delegate(object sender,DrawItemEventArgs e) {
                if(e.Bounds.Width<=0||e.Bounds.Height<=0)return;
                using(SolidBrush brush=new SolidBrush(Surface))e.Graphics.FillRectangle(brush,e.Bounds);
                int index=e.Index>=0?e.Index:combo.SelectedIndex;
                string text=index>=0&&index<combo.Items.Count?combo.Items[index].ToString():"";
                TextRenderer.DrawText(e.Graphics,text,combo.Font,e.Bounds,combo.Enabled?Color.White:Color.Gray,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            parent.Controls.Add(combo);
        }
        private static void ConfigureButton(Button b,string text,EventHandler handler) { b.Text=text; b.Width=126; b.Height=34; b.FlatStyle=FlatStyle.Flat; b.BackColor=Surface; b.ForeColor=Color.White; b.Margin=new Padding(0,0,5,0); b.Click+=handler; }
        private static void AddCheck(Control parent,string text,bool initial,Action<bool> changed) { CheckBox c=new CheckBox { Text=text,Checked=initial,AutoSize=true,Margin=new Padding(0,4,12,0) }; c.CheckedChanged+=delegate { changed(c.Checked); }; parent.Controls.Add(c); }
        private static Control ConfigurePath(Control parent,string label,TextBox box,Action browse)
        {
            AddLabel(parent,label); Control caption=parent.Controls[parent.Controls.Count-1]; FlowLayoutPanel row=Row(); row.Tag=caption; row.Height=31; box.Width=browse==null?335:297; box.BackColor=Surface; box.ForeColor=Color.White; box.BorderStyle=BorderStyle.FixedSingle;
            row.Controls.Add(box);
            if(browse!=null) { Button b=new Button { Text="...",Width=35,Height=27,FlatStyle=FlatStyle.Flat,Margin=new Padding(0) }; b.Click+=delegate { browse(); }; row.Controls.Add(b); }
            parent.Controls.Add(row); return row;
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
            connectionSetup.Enabled=profilePath.Parent.Enabled=!value&&!running;
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
            string previous=(devices.SelectedItem as Device)==null ? settings.Connection(activeEmulator).Serial : ((Device)devices.SelectedItem).Serial;
            try
            {
                string address=null;
                instances.Items.Clear(); devices.Items.Clear();
                if(activeEmulator==EmulatorKind.GameLoop)
                {
                    instances.Items.AddRange(Emulators.GameLoopWindows().ToArray());
                    address=Emulators.ValidateEndpoint(endpoint.Text); endpoint.Text=address;
                    CommandResult connected=await Task.Run(()=>Commands.Run(adb,new[] { "connect",address },10000,CancellationToken.None));
                    connected.EnsureSuccess(); WriteLog("GameLoop ADB: "+connected.Text);
                }
                else if (File.Exists(console))
                {
                    CommandResult listed=await Task.Run(()=>Commands.Run(console,new[] { "list2" },10000,CancellationToken.None)); listed.EnsureSuccess();
                    instances.Items.AddRange(Instance.Parse(listed.Text).ToArray());
                }
                if (instances.Items.Count>0) instances.SelectedIndex=0;
                CommandResult result=await Task.Run(()=>Commands.Run(adb,new[] { "devices","-l" },15000,CancellationToken.None)); result.EnsureSuccess();
                List<Device> found=Adb.ParseDevices(result.Text);
                if(address!=null) found=found.Where(d=>d.Serial==address).ToList();
                else if(Emulators.GameLoopWindows().Count>0)
                {
                    EmulatorConnection gameLoop=settings.Connection(EmulatorKind.GameLoop);
                    found=found.Where(d=>d.Serial!=gameLoop.Endpoint&&d.Serial!=gameLoop.Serial).ToList();
                }
                devices.Items.Clear(); devices.Items.AddRange(found.ToArray());
                Device preferred=found.FirstOrDefault(d=>d.Serial==previous) ?? found.FirstOrDefault(d=>d.State=="device") ?? found.FirstOrDefault(); if (preferred!=null) devices.SelectedItem=preferred;
                SaveConnection(); settings.Save();
                WriteLog("ADB: "+found.Count+" device(s); "+Emulators.Name(activeEmulator)+": "+instances.Items.Count+" running window(s). Select the serial and its matching window.");
                status.Text=found.Any(d=>d.State=="device") ? Emulators.Name(activeEmulator)+" connected / run game inspection" : "No connected device / start "+Emulators.Name(activeEmulator)+" and check ADB";
                if (!String.IsNullOrWhiteSpace(result.Error)) WriteLog(result.Error);
            }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private async Task Inspect()
        {
            if (busy) return; SetBusy(true);
            try { Adb adb=SelectedAdb(); Profile profile=Profile.Load(profilePath.Text); string text=await Task.Run(()=>Diagnostics.Run(adb,profile)); WriteLog(text); status.Text="Inspection complete / see results below"; }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private void StartDemo()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
            demoMode=true; demoClock.Restart(); overlay.Hide(); latest=Demo.Make(0); status.Text="DEMO / generated actors only / no game data"; WriteLog("Demo started. All displayed positions and labels are synthetic."); SetBusy(false);
        }
        internal async Task StartLive()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return; SetBusy(true);
            try
            {
                Profile p=Profile.Load(profilePath.Text); p.Validate(); Adb adb=SelectedAdb();
                Instance target=instances.SelectedItem as Instance;
                if (target==null) throw new IOException("Select the matching running "+Emulators.Name(activeEmulator)+" game window.");
                if (!Native.IsWindow(target.Window)) throw new IOException("The game render surface is unavailable. Restore the emulator game window and refresh devices.");
                demoMode=false; latest=null; frameBuffer.Clear(); overlay.Hide();
                if (readerCancel!=null) readerCancel.Dispose();
                readerCancel=new CancellationTokenSource(); CancellationToken token=readerCancel.Token;
                GameInfo info=await Task.Run(()=>GameInfo.Inspect(adb,p.Package,false,token));
                GameCompatibility.Ensure(p,info,Emulators.Name(activeEmulator));
                info.Maps=await Task.Run(()=>adb.ReadMaps(info.Pid,token));
                List<MapRegion> maps=MapRegion.Parse(info.Maps); ulong baseAddress=MapRegion.ModuleBase(maps,p.Module);
                AndroidMemory memory=new AndroidMemory(adb,info.Pid,maps,token);
                await Task.Run(()=>memory.Read(baseAddress,64));
                NativeReader nativeReader=null;
                if (p.Backend=="Native") { await Task.Run(()=>NativeReader.Prepare(adb,p,info,maps,token)); nativeReader=new NativeReader(adb,info.Pid,baseAddress); }
                GameReader reader=p.Backend=="Native"?null:new GameReader(p,memory,baseAddress); overlay.Target=target.Window; overlay.Suppressed=false;
                StopAimInput();
                WriteLog("Aim input: Windows Mouse (no touchscreen gestures). Tool integrity: 0x"+WindowsInputAccess.CurrentIntegrity.ToString("X")+". GameLoop may require restarting this tool as administrator.");
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
                        if (!closing && !IsDisposed) try { BeginInvoke(new Action(delegate { if(readerCancel==null||readerCancel.Token!=token)return;StopAimInput();overlay.Hide(); status.Text="Live reader stopped / see log"; SetBusy(false); })); } catch (InvalidOperationException) { }
                    }
                });
                WriteLog("Live reader started for "+p.Package+" "+info.VersionName+"; PID "+info.Pid+"; base 0x"+baseAddress.ToString("X")+". Overlay appears while the selected "+Emulators.Name(activeEmulator)+" window is foreground."); status.Text="LIVE / press F9 to stop";
                IntPtr rootWindow=Native.GetAncestor(target.Window,2);
                if(Native.IsIconic(rootWindow)) Native.ShowWindowAsync(rootWindow,9);
                Native.SetForegroundWindow(rootWindow);
            }
            catch (Exception ex) { if (readerCancel!=null && (readerTask==null||readerTask.IsCompleted)) { readerCancel.Dispose(); readerCancel=null; } Failure(ex); }
            finally { SetBusy(false); }
        }
        public async Task StopReader()
        {
            StopAimInput();
            liveDisplay=false; overlay.Suppressed=true; demoMode=false; latest=null; frameBuffer.Clear(); overlay.Hide();
            UpdateQuickBindings();
            if (readerCancel!=null) readerCancel.Cancel();
            if (busy && readerTask==null) { status.Text="Cancelling startup..."; return; }
            if (readerTask!=null) { SetBusy(true); try { await readerTask; } catch (OperationCanceledException) { } readerTask=null; }
            if (readerCancel!=null) { readerCancel.Dispose(); readerCancel=null; }
            latest=null; frameBuffer.Clear();
            status.Text="Stopped"; WriteLog("Display and reader stopped."); SetBusy(false);
        }
        private void Failure(Exception ex) { status.Text="Action failed / see log"; WriteLog(ex.Message); }
        private void RestartAsAdministrator()
        {
            try
            {
                SaveOptions();
                Process next=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--emulator "+activeEmulator+" --live --restart-elevated") { UseShellExecute=true,Verb="runas",WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory });
                if(next!=null) { next.Dispose();Close(); }
            }
            catch(System.ComponentModel.Win32Exception ex) { WriteLog(ex.NativeErrorCode==1223?"Administrator restart was cancelled in Windows. Input remains blocked while the emulator has higher privileges.":ex.Message); }
            catch(Exception ex) { Failure(ex); }
        }
        private void StopAimInput()
        {
            aim.SetInput(new MouseInput());
        }
        private void SaveOptions() { SaveConnection(); settings.Options=options; settings.Save(); }
        private bool GameFocused()
        {
            return liveDisplay&&!demoMode&&!closing&&!menuOpen&&overlay.Target!=IntPtr.Zero&&Native.IsWindow(overlay.Target)&&Native.GetForegroundWindow()==Native.GetAncestor(overlay.Target,2);
        }
        private void UpdateQuickBindings()
        {
            bool game=GameFocused();
            if(!game)overlay.Quick.Open=false;
            if(game!=quickKeysActive)
            {
                quickKeysActive=game;
                for(int key=3;key<=6;key++)
                {
                    int id=key+1;
                    if(game)
                    {
                        overlay.Quick.Unavailable.Remove(key);
                        if(Native.RegisterHotKey(Handle,id,0x4000,(uint)(0x6f+key)))quickRegistered.Add(id);
                        else { overlay.Quick.Unavailable.Add(key);WriteLog("F"+key+" is already in use; its quick action is unavailable."); }
                    }
                    else if(quickRegistered.Remove(id))Native.UnregisterHotKey(Handle,id);
                }
                if(game)
                {
                    fullMenuHotkey=Native.RegisterHotKey(Handle,8,0x4000|2,0x76);
                    if(!fullMenuHotkey)WriteLog("Ctrl+F7 is already in use; the on-screen full-menu button remains available.");
                }
                else if(fullMenuHotkey) { Native.UnregisterHotKey(Handle,8);fullMenuHotkey=false; }
            }
            bool arrows=game&&overlay.Quick.Open;
            if(arrows!=quickArrowsActive)
            {
                quickArrowsActive=arrows;
                uint[] keys={0x25,0x27,0x26,0x28};
                for(int i=0;i<keys.Length;i++)
                {
                    int id=20+i;
                    if(arrows)
                    {
                        if(Native.RegisterHotKey(Handle,id,0,keys[i]))quickRegistered.Add(id);
                        else WriteLog("Quick panel arrow is already in use; F7 still closes the panel.");
                    }
                    else if(quickRegistered.Remove(id))Native.UnregisterHotKey(Handle,id);
                }
            }
        }
        private void ChangedQuickOptions()
        {
            aim.Reset();quickSavePending=true;quickSaveDue=DateTime.UtcNow.AddMilliseconds(300);overlay.Invalidate();scene.Invalidate();
        }
        private void OpenMenu()
        {
            if(menuOpen||closing)return;menuOpen=true;
            UpdateQuickBindings();
            aim.Reset();
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
                else if (m.WParam.ToInt32()==3)
                {
                    if(GameFocused()) { overlay.Quick.Open=!overlay.Quick.Open;UpdateQuickBindings();overlay.Invalidate(); }
                    else OpenMenu();
                }
                else if(m.WParam.ToInt32()==8)OpenMenu();
                else if(m.WParam.ToInt32()>=4&&m.WParam.ToInt32()<=7&&GameFocused()) { QuickActions.Apply(options,m.WParam.ToInt32()-1);ChangedQuickOptions(); }
                else if(m.WParam.ToInt32()>=20&&m.WParam.ToInt32()<=23&&GameFocused()&&overlay.Quick.Open)
                {
                    int id=m.WParam.ToInt32();
                    if(id==22)overlay.Quick.SelectedRow=(overlay.Quick.SelectedRow+2)%3;
                    else if(id==23)overlay.Quick.SelectedRow=(overlay.Quick.SelectedRow+1)%3;
                    else { QuickActions.Adjust(options,overlay.Quick.SelectedRow,id==20?-1:1);ChangedQuickOptions(); }
                    overlay.Invalidate();
                }
            }
            base.WndProc(ref m);
        }
        private async void OnClosing(object sender,FormClosingEventArgs e)
        {
            if (closing) return;
            e.Cancel=true; closing=true; liveDisplay=false; overlay.Suppressed=true; overlay.Hide(); timer.Stop();StopAimInput(); if (readerCancel!=null) readerCancel.Cancel();
            UpdateQuickBindings();
            if (readerTask!=null) try { await readerTask; } catch { }
            if (toggleHotkey) Native.UnregisterHotKey(Handle,1); if (stopHotkey) Native.UnregisterHotKey(Handle,2); overlay.Close();
            if(menuHotkey)Native.UnregisterHotKey(Handle,3);
            if(fullMenuHotkey)Native.UnregisterHotKey(Handle,8);
            Native.timeEndPeriod(1);
            if (readerCancel!=null) readerCancel.Dispose();
            SaveConnection();
            settings.Options=options;
            try { settings.Save(); } catch { }
            BeginInvoke(new Action(Close));
        }
    }
}
