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
        public override string ToString() { return Index+" / "+Title+(Emulator==EmulatorKind.GameLoop&&Window==IntPtr.Zero?" ["+L.Text("Render surface unavailable")+"]":""); }
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
        public string Language = "ar";
        public EmulatorConnection LDPlayer, GameLoop;
        public RenderOptions Options=new RenderOptions();
        public EmulatorConnection Connection(EmulatorKind kind)
        {
            if (LDPlayer==null) LDPlayer=new EmulatorConnection { AdbPath=AdbPath,ConsolePath=ConsolePath,Serial=Serial,ProfilePath=ProfilePath };
            else if(String.IsNullOrEmpty(LDPlayer.ProfilePath)) LDPlayer.ProfilePath=ProfilePath;
            if (GameLoop==null) GameLoop=new EmulatorConnection();
            return kind==EmulatorKind.GameLoop?GameLoop:LDPlayer;
        }
        private static string LegacyFilePath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings.json"); } }
        public static string FilePath
        {
            get
            {
#if DISTRIBUTION
                return Path.Combine(UserData.Folder, "settings.json");
#else
                return LegacyFilePath;
#endif
            }
        }
        public static Settings Load() { try { return new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(File.Exists(FilePath)?FilePath:LegacyFilePath)) ?? new Settings(); } catch { return new Settings(); } }
        public void Save() { UserData.Replace(FilePath,System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this))); }
    }
    public sealed partial class MainForm : Form
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
        private bool demoMode, busy, closing, toggleHotkey, stopHotkey, menuHotkey;
        private bool fullMenuHotkey,quickKeysActive,quickArrowsActive,quickSavePending;
        private bool shortcutsSuspended;
        private readonly bool previewOnly;
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
            return new { Open=overlay.Quick.Open,Collapsed=overlay.Quick.Collapsed,Row=overlay.Quick.SelectedRow,Aim=options.Aim.Enabled,Mode=options.Aim.Activation,Point=options.Aim.AimPoint,Strength=options.Aim.Strength,Radius=options.Aim.Radius,Scale=options.Aim.InputScale,Esp=options.EspEnabled,GameFocused=GameFocused(),Registered=quickRegistered.OrderBy(i=>i).ToArray(),Unavailable=overlay.Quick.Unavailable.OrderBy(i=>i).ToArray(),SavePending=quickSavePending };
        }
        private static readonly Color Background = SpectraTheme.Background, Surface = SpectraTheme.Surface;
        public MainForm(bool autoStart=false, EmulatorKind? selectedEmulator=null,bool renderOnly=false)
        {
            previewOnly=renderOnly;
            settings = Settings.Load();
            L.UseLanguage(settings.Language);
            options=settings.Options??new RenderOptions(); options.EnsureDefaults();
            Text = L.ProductName; Size = new Size(1150,920); MinimumSize = new Size(1020,850);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Background; ForeColor = Color.Gainsboro; Font = new Font("Segoe UI",10);
            scene = new ScenePanel(options) { Dock = DockStyle.Fill, Margin = new Padding(12,0,0,0) };
            overlay = new Overlay(options);
            TableLayoutPanel layout = new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=5 };
            layout.BackColor=Background;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,390)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,86)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,150)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32));
            Controls.Add(layout);
            TabControl tabs=new TabControl { Dock=DockStyle.Fill,Margin=new Padding(0,0,12,0) };
            TabPage connectionTab=new TabPage() { BackColor=Background,ForeColor=Color.Gainsboro }, displayTab=new TabPage() { BackColor=Background,ForeColor=Color.Gainsboro };
            tabs.TabPages.Add(connectionTab); tabs.TabPages.Add(displayTab);
            FlowLayoutPanel setup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            FlowLayoutPanel displaySetup = new FlowLayoutPanel { Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(4,0,0,0),BackColor=Background };
            connectionTab.Controls.Add(setup); displayTab.Controls.Add(displaySetup);
            connectionSetup=setup;
            layout.Controls.Add(tabs,0,1); layout.Controls.Add(scene,1,1);
            AddLabel(setup,"Emulator connection");
            AddLabel(setup,"Emulator"); ConfigureCombo(setup,emulators); emulators.Items.AddRange(new object[] { "LDPlayer 14", "GameLoop" });
            ConfigurePath(setup,"ADB executable",adbPath,delegate { BrowseExecutable(adbPath,"adb.exe"); });
            consoleRow=ConfigurePath(setup,"LDPlayer console",consolePath,delegate { BrowseExecutable(consolePath,"ldconsole.exe"); });
            endpointRow=ConfigurePath(setup,"GameLoop ADB address",endpoint,null);
            AddLabel(setup,"ADB device (actual serial)"); ConfigureCombo(setup,devices);
            AddLabel(setup,"Overlay target (select matching instance)"); ConfigureCombo(setup,instances);
            FlowLayoutPanel connectionButtons = Row(); ConfigureButton(scan,"تحديث الأجهزة",async delegate { await Scan(); }); ConfigureButton(inspect,"فحص اللعبة",async delegate { await Inspect(); }); connectionButtons.Controls.Add(scan); connectionButtons.Controls.Add(inspect); setup.Controls.Add(connectionButtons);
            AddLabel(displaySetup,"JSON profile");
            ConfigurePath(displaySetup,"JSON profile",profilePath,delegate {
                using (OpenFileDialog dialog = new OpenFileDialog { Filter=L.Text("JSON profile (*.json)|*.json"),InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles") }) if (dialog.ShowDialog(this)==DialogResult.OK) profilePath.Text=dialog.FileName;
            });
            Label note = new Label { Width=340,Height=72,ForeColor=Color.FromArgb(237,188,104),Margin=new Padding(0,12,0,4) }; BindText(note,"ملفا 4.6.0 للبناءين 21525 و21518 مرتبطان ببصمة المكتبة. Live يقرأ بيانات فعلية؛ العرض التجريبي يستخدم بيانات مصطنعة."); displaySetup.Controls.Add(note);
            FlowLayoutPanel checks = new FlowLayoutPanel { Width=340,Height=90,Margin=Padding.Empty };
            AddCheck(checks,"Boxes",options.Boxes,v=>options.Boxes=v); AddCheck(checks,"الصحة",options.Health,v=>options.Health=v); AddCheck(checks,"Labels",options.Names,v=>options.Names=v); AddCheck(checks,"المسافة",options.Distance,v=>options.Distance=v); AddCheck(checks,"Top lines",options.Lines,v=>options.Lines=v); AddCheck(checks,"Alerts",options.Alerts,v=>options.Alerts=v); AddCheck(checks,"أفراد الفريق",options.Allies,v=>options.Allies=v,true); displaySetup.Controls.Add(checks);
            FlowLayoutPanel range = Row(); AddLabel(range,"Max distance (m)");
            NumericUpDown distance = new NumericUpDown { Minimum=10,Maximum=5000,Value=(decimal)options.MaxDistance,Width=90,BackColor=Surface,ForeColor=Color.White }; distance.ValueChanged+=delegate { options.MaxDistance=(float)distance.Value; }; range.Controls.Add(distance); displaySetup.Controls.Add(range);
            FlowLayoutPanel runButtons = Row(); ConfigureButton(demo,"عرض تجريبي",delegate { StartDemo(); }); ConfigureButton(live,"تشغيل Live",async delegate { await StartFromHome(); }); ConfigureButton(stop,"إيقاف",async delegate { await StopReader(); }); stop.Width=70;
            Button menu=new Button(); ConfigureButton(menu,"القائمة الكاملة",delegate { OpenMenu(); }); menu.Width=160;
            Button administrator=new Button();ConfigureButton(administrator,"تشغيل كمسؤول",delegate { RestartAsAdministrator(); });administrator.Width=145;
            runButtons.Width=1050; runButtons.Controls.Add(demo); runButtons.Controls.Add(live); runButtons.Controls.Add(stop); runButtons.Controls.Add(menu);runButtons.Controls.Add(administrator); AddLabel(runButtons,"Insert: collapse / expand"); layout.Controls.Add(runButtons,0,2); layout.SetColumnSpan(runButtons,2);
            AddLabel(setup,"التشخيص يقرأ بيانات الاتصال والذاكرة فقط.");
            AddLabel(setup,"Live يحتاج ملف إزاحات متحقق من هذا الإصدار.");
            log.Dock=DockStyle.Fill; log.Multiline=true; log.ReadOnly=true; log.ScrollBars=ScrollBars.Vertical; log.BackColor=Color.FromArgb(10,16,24); log.ForeColor=Color.Silver; log.BorderStyle=BorderStyle.FixedSingle; log.Font=new Font("Consolas",9); log.Margin=new Padding(0,12,0,0);
            layout.Controls.Add(log,0,3); layout.SetColumnSpan(log,2);
            status.Dock=DockStyle.Fill; status.TextAlign=ContentAlignment.MiddleLeft; status.ForeColor=Painter.Accent; layout.Controls.Add(status,0,4); layout.SetColumnSpan(status,2);
            BuildSpectraShell(layout);
            activeEmulator=selectedEmulator??(settings.Emulator=="GameLoop"?EmulatorKind.GameLoop:EmulatorKind.LDPlayer);
            emulators.SelectedIndex=activeEmulator==EmulatorKind.GameLoop?1:0;
            LoadConnection();
            emulators.SelectedIndexChanged+=async delegate {
                SaveConnection(); activeEmulator=emulators.SelectedIndex==1?EmulatorKind.GameLoop:EmulatorKind.LDPlayer;
                demoMode=false; liveDisplay=false; latest=null; frameBuffer.Clear(); overlay.Hide();
                devices.Items.Clear(); instances.Items.Clear(); LoadConnection();
                settings.Emulator=activeEmulator.ToString(); SaveOptions(); await Scan();
            };
            SetStatus("Idle / waiting for connection");
            Native.timeBeginPeriod(1);
            timer.Interval=Math.Max(1,1000/options.RefreshRate); timer.Tick+=delegate {
                int requestedInterval=Math.Max(1,1000/Math.Max(30,options.RefreshRate)); if(timer.Interval!=requestedInterval) timer.Interval=requestedInterval;
                if (demoMode) latest=Demo.Make(demoClock.Elapsed.TotalSeconds);
                if (liveDisplay&&!demoMode&&Painter.Fresh(latest)) SetStatus("LIVE / {0} entities / {1:0} Hz / {2} menu / {3} stop",latest.Actors.Length,latest.ReadHertz,options.Shortcuts.Label(8),options.Shortcuts.Label(2));
                UpdateQuickBindings();
                if(quickSavePending&&DateTime.UtcNow>=quickSaveDue)
                {
                    quickSavePending=false;try { SaveOptions(); }catch(Exception ex) { WriteLogText("Quick settings could not be saved: {0}",ex.Message); }
                }
                Frame displayed=demoMode?latest:frameBuffer.Sample();
                scene.Frame=demoMode||liveDisplay?displayed:null; scene.Invalidate(); overlay.UpdateFrame(demoMode||!liveDisplay ? null : displayed);
                if(!aim.Update(latest,overlay.Target,options.Aim,liveDisplay&&!demoMode&&!closing&&!overlay.Suppressed)) WriteLogText("Aim input transport failed. Aim disabled; restart Live and check the selected input method.");
                overlay.AimState=aim.State;
                overlay.AimCandidate=aim.Candidate;
                overlay.AimMovements=aim.DeliveredMovements;
            }; timer.Start();
            Shown+=async delegate {
                if(renderOnly)return;
                if(options.ImageQualityEnabled)
                {
                    try { NvidiaGraphics.SetFxaa(true); }
                    catch(Exception ex) { options.ImageQualityEnabled=false;WriteLogText("Image quality could not be synchronized: {0}",ex.Message);SaveOptions(); }
                }
                RebindShortcuts();
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
        private void AddLabel(Control parent,string text)
        {
            var label = new Label { AutoSize=true,Margin=new Padding(0,9,0,4),ForeColor=Color.Silver };
            BindText(label,text);
            parent.Controls.Add(label);
        }
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
        private void ConfigureButton(Button b,string text,EventHandler handler) { BindText(b,text);b.AutoSize=true; b.Width=126; b.Height=34; b.FlatStyle=FlatStyle.Flat; b.BackColor=Surface; b.ForeColor=Color.White; b.Margin=new Padding(0,0,5,0); b.Click+=handler; }
        private void AddCheck(Control parent,string text,bool initial,Action<bool> changed, bool developing=false) { CheckBox c=new CheckBox { Checked=initial,AutoSize=true,Margin=new Padding(0,4,12,0) }; BindText(c,text,developing);c.CheckedChanged+=delegate { changed(c.Checked); }; parent.Controls.Add(c); }
        private Control ConfigurePath(Control parent,string label,TextBox box,Action browse)
        {
            AddLabel(parent,label); Control caption=parent.Controls[parent.Controls.Count-1]; FlowLayoutPanel row=Row(); row.Tag=caption; row.Height=31; box.Width=browse==null?335:297; box.BackColor=Surface; box.ForeColor=Color.White; box.BorderStyle=BorderStyle.FixedSingle;
            row.Controls.Add(box);
            if(browse!=null) { Button b=new Button { Text="...",Width=35,Height=27,FlatStyle=FlatStyle.Flat,Margin=new Padding(0) }; b.Click+=delegate { browse(); }; row.Controls.Add(b); }
            parent.Controls.Add(row); return row;
        }
        private void BrowseExecutable(TextBox target,string name) { using (OpenFileDialog d=new OpenFileDialog { Filter=L.Text("Executable (*.exe)|*.exe"),FileName=name }) if (d.ShowDialog(this)==DialogResult.OK) target.Text=d.FileName; }

        private readonly Dictionary<Control, Func<string>> localizedControls = new Dictionary<Control, Func<string>>();
        private readonly List<Tuple<DateTime, string, object[]>> logEntries = new List<Tuple<DateTime, string, object[]>>();
        private string statusKey = "Idle / waiting for connection";
        private object[] statusValues = new object[0];

        private void BindText(Control control, string key, bool developing = false)
        {
            localizedControls[control] = () => developing ? L.InDevelopment(key) : L.Text(key);
            control.Text = localizedControls[control]();
            control.RightToLeft = L.Direction;
        }

        private void SetStatus(string key, params object[] values)
        {
            statusKey = key;
            statusValues = values;
            status.Text = L.Format(key, values);
        }

        private void ChangeLanguage(int index)
        {
            string previous = settings.Language;
            settings.Language = L.CodeAt(index);
            L.UseLanguage(settings.Language);
            try { SaveOptions(); }
            catch (Exception ex)
            {
                settings.Language = previous;
                L.UseLanguage(previous);
                languageChoice.SelectedIndex = L.Index;
                MessageBox.Show(this, L.Text("Could not save the language setting.") + Environment.NewLine + ex.Message,
                    L.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            RefreshLanguage();
        }

        private void RefreshLanguage()
        {
            foreach (Control control in localizedControls.Keys.ToArray())
            {
                if (control.IsDisposed) { localizedControls.Remove(control); continue; }
                control.Text = localizedControls[control]();
                control.RightToLeft = L.Direction;
            }
            SetStatus(statusKey, statusValues);
            RefreshLog();
            dashboard.RefreshLanguage();
            if (optionsFactory != null) { optionsFactory.Dispose(); optionsFactory = null; }
            NavigateShell(activeShellPage);
#if DISTRIBUTION
            RefreshDistributionLanguage();
#endif
            overlay.Invalidate();
            scene.Invalidate();
            Invalidate(true);
        }

        private void WriteLogText(string key, params object[] values)
        {
            AddLogEntry(key, values);
        }

        private void AddLogEntry(string message, object[] values)
        {
            if (IsDisposed || closing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string, object[]>(AddLogEntry), message, values); }
                catch (InvalidOperationException) { }
                return;
            }
            logEntries.Add(Tuple.Create(DateTime.Now, message, values));
            if (logEntries.Count > 200) logEntries.RemoveAt(0);
            RefreshLog();
        }

        private void RefreshLog()
        {
            log.Text = String.Join(Environment.NewLine, logEntries.Select(entry =>
                entry.Item1.ToString("HH:mm:ss", L.Culture) + "  " +
                (entry.Item3 == null ? entry.Item2 : L.Format(entry.Item2, entry.Item3)))) + Environment.NewLine;
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }

        private void WriteLog(string message)
        {
            AddLogEntry(message, null);
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
            if (d==null || d.State!="device") throw new IOException(L.Text("Select a connected and authorized ADB device, then scan again if needed."));
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
                WriteLogText("ADB: {0} device(s); {1}: {2} running window(s). Select the serial and its matching window.",found.Count,Emulators.Name(activeEmulator),instances.Items.Count);
                SetStatus(found.Any(d=>d.State=="device") ? "{0} connected / run game inspection" : "No connected device / start {0} and check ADB", Emulators.Name(activeEmulator));
                if (!String.IsNullOrWhiteSpace(result.Error)) WriteLog(result.Error);
            }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private async Task Inspect()
        {
            if (busy) return; SetBusy(true);
            try { Adb adb=SelectedAdb(); Profile profile=Profile.Load(profilePath.Text); string text=await Task.Run(()=>Diagnostics.Run(adb,profile)); WriteLog(text); SetStatus("Inspection complete / see results below"); }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private void StartDemo()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
            demoMode=true; demoClock.Restart(); overlay.Hide(); latest=Demo.Make(0); SetStatus("DEMO / generated actors only / no game data"); WriteLogText("Demo started. All displayed positions and labels are synthetic."); SetBusy(false);
        }
        internal async Task StartLive()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return; SetBusy(true);
            try
            {
                Profile p=Profile.Load(profilePath.Text); p.Validate(); Adb adb=SelectedAdb();
                Instance target=instances.SelectedItem as Instance;
                if (target==null) throw new IOException(L.Format("Select the matching running {0} game window.",Emulators.Name(activeEmulator)));
                if (!Native.IsWindow(target.Window)) throw new IOException(L.Text("The game render surface is unavailable. Restore the emulator game window and refresh devices."));
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
                WriteLogText("Aim input: Windows Mouse (no touchscreen gestures). Tool integrity: 0x{0:X}. GameLoop may require restarting this tool as administrator.",WindowsInputAccess.CurrentIntegrity);
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
                    catch (Exception ex) { WriteLogText("LIVE STOPPED: {0}",ex.Message); }
                    finally
                    {
                        liveDisplay=false; latest=null; frameBuffer.Clear();
                        if (!closing && !IsDisposed) try { BeginInvoke(new Action(delegate { if(readerCancel==null||readerCancel.Token!=token)return;StopAimInput();overlay.Hide(); SetStatus("Live reader stopped / see log"); SetBusy(false); })); } catch (InvalidOperationException) { }
                    }
                });
                WriteLogText("Live reader started for {0} {1}, PID {2}, base 0x{3:X}. The overlay appears while the selected {4} window is active.",p.Package,info.VersionName,info.Pid,baseAddress,Emulators.Name(activeEmulator)); SetStatus("LIVE / {0} to stop",options.Shortcuts.Label(2));
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
            if (busy && readerTask==null) { SetStatus("Cancelling startup..."); return; }
            if (readerTask!=null) { SetBusy(true); try { await readerTask; } catch (OperationCanceledException) { } readerTask=null; }
            if (readerCancel!=null) { readerCancel.Dispose(); readerCancel=null; }
            latest=null; frameBuffer.Clear();
            SetStatus("Stopped"); WriteLogText("Display and reader stopped."); SetBusy(false);
        }
        private void Failure(Exception ex) { SetStatus("Action failed / see log"); WriteLog(ex.Message); }
        private async Task StartFromHome()
        {
            if(!live.Enabled||previewOnly)return;
            if(AdministratorLaunch.NeedsElevation(WindowsInputAccess.CurrentIntegrity))RestartAsAdministrator();else await StartLive();
        }
        private void RestartAsAdministrator()
        {
            try
            {
                SaveOptions();
                Process next=Process.Start(AdministratorLaunch.StartInfo(Application.ExecutablePath,activeEmulator,AppDomain.CurrentDomain.BaseDirectory));
                if(next!=null) { next.Dispose();Close(); }
            }
            catch(System.ComponentModel.Win32Exception ex) { if(ex.NativeErrorCode==1223)WriteLogText("Administrator restart was cancelled in Windows. Input remains blocked while the emulator has higher privileges.");else WriteLog(ex.Message); }
            catch(Exception ex) { Failure(ex); }
        }
        private void StopAimInput()
        {
            aim.SetInput(new MouseInput());
        }
        private void SaveOptions() { if(previewOnly)return;SaveConnection(); settings.Options=options; settings.Save(); }
        private string ApplyImageQuality(bool enabled)
        {
            bool previous=options.ImageQualityEnabled;
            try { NvidiaGraphics.SetFxaa(enabled,delegate { options.ImageQualityEnabled=enabled;SaveOptions(); }); }
            catch { options.ImageQualityEnabled=previous;throw; }
            ChangedQuickOptions();
            string message=enabled?"تنعيم الأداة مفعّل، وتم حفظ طلب FXAA والتحقق منه. أعد تشغيل المحاكي؛ تأثيره على اللعبة غير مؤكد.":"تنعيم الأداة متوقف، وتم حفظ FXAA = Off والتحقق منه. أعد تشغيل المحاكي لتطبيق الإيقاف عليه.";
            WriteLogText(message);return L.Text(message);
        }
        private bool GameFocused()
        {
            return liveDisplay&&!demoMode&&!closing&&overlay.Target!=IntPtr.Zero&&Native.IsWindow(overlay.Target)&&Native.GetForegroundWindow()==Native.GetAncestor(overlay.Target,2);
        }
        private void UpdateQuickBindings()
        {
            if(shortcutsSuspended)return;
            bool game=GameFocused();
            if(!game)overlay.Quick.Open=false;
            if(game!=quickKeysActive)
            {
                quickKeysActive=game;
                for(int id=4;id<=9;id++)
                {
                    if(game)
                    {
                        RegisterShortcut(id);
                    }
                    else if(quickRegistered.Remove(id))Native.UnregisterHotKey(Handle,id);
                }
            }
            bool arrows=game&&overlay.Quick.Open&&!overlay.Quick.Collapsed;
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
                        else WriteLogText("Quick panel arrow is already in use. {0} still closes the panel.",options.Shortcuts.Label(3));
                    }
                    else if(quickRegistered.Remove(id))Native.UnregisterHotKey(Handle,id);
                }
            }
        }
        private bool RegisterShortcut(int id)
        {
            Keys key=id==9?Keys.Insert:options.Shortcuts.Key(id);
            uint modifiers=id==9?0x4000u:options.Shortcuts.Modifiers(id);
            overlay.Quick.Unavailable.Remove(id);
            if(Native.RegisterHotKey(Handle,id,modifiers,(uint)(key&Keys.KeyCode))) { quickRegistered.Add(id);return true; }
            overlay.Quick.Unavailable.Add(id);WriteLogText("{0} is already in use. Change it in Shortcuts.",ShortcutOptions.Format(key));return false;
        }
        private void ReleaseShortcuts()
        {
            foreach(int id in quickRegistered)Native.UnregisterHotKey(Handle,id);
            quickRegistered.Clear();quickKeysActive=quickArrowsActive=false;toggleHotkey=stopHotkey=menuHotkey=fullMenuHotkey=false;
        }
        private void RebindShortcuts()
        {
            ReleaseShortcuts();overlay.Quick.Unavailable.Clear();shortcutsSuspended=false;
            toggleHotkey=RegisterShortcut(1);stopHotkey=RegisterShortcut(2);menuHotkey=RegisterShortcut(3);UpdateQuickBindings();
        }
        private void CaptureShortcuts(bool capturing)
        {
            if(capturing) { ReleaseShortcuts();shortcutsSuspended=true; }
            else if(!closing)RebindShortcuts();
        }
        private void ChangedQuickOptions()
        {
            aim.Reset();quickSavePending=true;quickSaveDue=DateTime.UtcNow.AddMilliseconds(300);overlay.Invalidate();scene.Invalidate();
        }
        private void OpenMenu(int page=0)
        {
            if(closing)return;aim.Reset();
            NavigateShell(page==8?5:page==1?2:page==7?3:1);
            if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;
            Show();Activate();Native.SetForegroundWindow(Handle);UpdateQuickBindings();
        }
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x0084&&FormBorderStyle==FormBorderStyle.None&&WindowState==FormWindowState.Normal)
            {
                base.WndProc(ref m);Point p=PointToClient(new Point((short)(m.LParam.ToInt64()&0xffff),(short)((m.LParam.ToInt64()>>16)&0xffff)));
                bool left=p.X<8,right=p.X>=ClientSize.Width-8,top=p.Y<8,bottom=p.Y>=ClientSize.Height-8;
                if(left||right||top||bottom)m.Result=new IntPtr(top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:11);return;
            }
            if (m.Msg==0x0312)
            {
                if (m.WParam.ToInt32()==1) { overlay.Suppressed=!overlay.Suppressed; WriteLogText(overlay.Suppressed?"Overlay hidden.":"Overlay enabled."); }
                else if (m.WParam.ToInt32()==2) { Task ignored=StopReader(); }
                else if (m.WParam.ToInt32()==3)
                {
                    if(GameFocused()) { if(overlay.Quick.Collapsed) { overlay.Quick.Collapsed=false;overlay.Quick.Open=true; }else overlay.Quick.Open=!overlay.Quick.Open;UpdateQuickBindings();overlay.Invalidate(); }
                    else OpenMenu();
                }
                else if(m.WParam.ToInt32()==8)OpenMenu();
                else if(m.WParam.ToInt32()==9&&GameFocused()) { overlay.Quick.Collapsed=!overlay.Quick.Collapsed;UpdateQuickBindings();overlay.Invalidate(); }
                else if(m.WParam.ToInt32()>=4&&m.WParam.ToInt32()<=7&&GameFocused()) { QuickActions.Apply(options,m.WParam.ToInt32()-1);ChangedQuickOptions(); }
                else if(m.WParam.ToInt32()>=20&&m.WParam.ToInt32()<=23&&GameFocused()&&overlay.Quick.Open&&!overlay.Quick.Collapsed)
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
            ReleaseShortcuts();
            if (readerTask!=null) try { await readerTask; } catch { }
            if (toggleHotkey) Native.UnregisterHotKey(Handle,1); if (stopHotkey) Native.UnregisterHotKey(Handle,2); overlay.Close();
            if(menuHotkey)Native.UnregisterHotKey(Handle,3);
            if(fullMenuHotkey)Native.UnregisterHotKey(Handle,8);
            Native.timeEndPeriod(1);
            if (readerCancel!=null) readerCancel.Dispose();
            SaveConnection();
            settings.Options=options;
            try { if(!previewOnly)settings.Save(); } catch { }
            BeginInvoke(new Action(Close));
        }
    }
}
