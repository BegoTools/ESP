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
#if DISTRIBUTION
        public int SchemaVersion;
#endif
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
        public void Save()
        {
#if DISTRIBUTION
            SchemaVersion = Math.Max(SchemaVersion, 2);
#endif
            UserData.Replace(FilePath,System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(this)));
        }
    }
    public sealed partial class MainForm : Form
    {
        private readonly TextBox adbPath = new TextBox(), consolePath = new TextBox(), endpoint = new TextBox(), profilePath = new TextBox(), log = new SpectraLog();
        private readonly SpectraChoice emulators=new SpectraChoice(),devices=new SpectraChoice(),instances=new SpectraChoice();
        private Control consoleRow,endpointRow,profileRow,connectionSetup;
        private EmulatorKind activeEmulator;
        private readonly Button scan=new SpectraButton(),inspect=new SpectraButton(),live=new SpectraButton(),demo=new SpectraButton(),stop=new SpectraButton();
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
        internal bool HasVisibleOverlay { get { return HasLiveFrame&&overlay.IsShown; } }
        internal object AimStatus()
        {
            AimTarget target=aim.Target;
            Frame decision=aim.DecisionFrame;
            return new { Enabled=options.Aim.Enabled,Strength=options.Aim.Strength,Radius=options.Aim.Radius,AimPoint=options.Aim.AimPoint,Activation=options.Aim.Activation,Backend=aim.InputDescription,ToolIntegrity=WindowsInputAccess.CurrentIntegrity,LocalScoped=decision==null?(bool?)null:decision.LocalScoped,LocalFiring=decision==null?(bool?)null:decision.LocalFiring,State=aim.State,SentMovements=aim.SentMovements,DeliveredMovements=aim.DeliveredMovements,Input=aim.InputState,Target=target==null?null:target.Address,Candidate=aim.Candidate==null?null:aim.Candidate.Address,Error=target==null?(double?)null:target.Error,CameraRotation=decision==null?(Vec3?)null:decision.CameraRotation };
        }
        internal object RuntimeStatus()
        {
            IntPtr target=overlay.Target;
            return new { Emulator=Emulators.Name(activeEmulator),SelectedEmulator=emulators.SelectionText,Profile=profilePath.Text,Serial=(devices.SelectedItem as Device)==null?"":((Device)devices.SelectedItem).Serial,Live=liveDisplay,FreshFrame=Painter.Fresh(latest),Entities=latest==null?0:latest.Actors.Length,Hertz=latest==null?0:latest.ReadHertz,RenderWindow=target.ToInt64(),RenderBounds=Native.ClientBounds(target),OverlayBounds=overlay.Bounds,OverlayVisible=overlay.IsShown,Foreground=Native.GetForegroundWindow().ToInt64(),TargetRoot=Native.GetAncestor(target,2).ToInt64() };
        }
        internal object QuickStatus()
        {
            return new { Open=overlay.Quick.Open,Collapsed=overlay.Quick.Collapsed,Hidden=overlay.Quick.Hidden,PanelVisible=overlay.Panel.Visible,PanelBounds=overlay.Panel.Bounds,Row=overlay.Quick.SelectedRow,Aim=options.Aim.Enabled,Mode=options.Aim.Activation,Point=options.Aim.AimPoint,Strength=options.Aim.Strength,Radius=options.Aim.Radius,Scale=options.Aim.InputScale,Esp=options.EspEnabled,GameFocused=GameFocused(),Registered=quickRegistered.OrderBy(i=>i).ToArray(),Unavailable=overlay.Quick.Unavailable.OrderBy(i=>i).ToArray(),SavePending=quickSavePending };
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
            overlay.Panel.ClickAction+=ProcessGamePanelAction;
            overlay.Panel.PositionChanged+=QueueQuickSave;
            overlay.Panel.AdjustmentChanged+=ChangedQuickOptions;
            BuildSpectraShell();
            activeEmulator=selectedEmulator??(settings.Emulator=="GameLoop"?EmulatorKind.GameLoop:EmulatorKind.LDPlayer);
            emulators.SelectedIndex=activeEmulator==EmulatorKind.GameLoop?1:0;
            LoadConnection();
            emulators.SelectedIndexChanged+=async delegate {
                SaveConnection(); activeEmulator=emulators.SelectedIndex==1?EmulatorKind.GameLoop:EmulatorKind.LDPlayer;
                demoMode=false; liveDisplay=false; latest=null; frameBuffer.Clear(); overlay.HideOverlay();
                devices.ReplaceItems(new object[0]); instances.ReplaceItems(new object[0]); LoadConnection();
                settings.Emulator=activeEmulator.ToString(); SaveOptions(); await Scan();
            };
            SetStatus("Idle / waiting for connection");
            Native.timeBeginPeriod(1);
            timer.Interval=Math.Max(1,1000/options.RefreshRate); timer.Tick+=delegate {
#if DISTRIBUTION
                if (license != null) SuspendUnlicensedFeatures();
#endif
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
                if(!aim.Update(latest,overlay.Target,options.Aim,liveDisplay&&!demoMode&&!closing&&!overlay.Suppressed&&!overlay.Panel.IsInteracting)) WriteLogText("Aim input transport failed. Aim disabled; restart Live and check the selected input method.");
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
            profilePath.Text=File.Exists(connection.ProfilePath)&&!Profile.IsBundledPath(connection.ProfilePath)?connection.ProfilePath:"";
            consoleRow.Visible=activeEmulator==EmulatorKind.LDPlayer;
            endpointRow.Visible=activeEmulator==EmulatorKind.GameLoop;
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
        private void ConfigureButton(Button button,string text,EventHandler handler)
        {
            BindText(button,text);
            button.AutoSize=true;
            button.MinimumSize=new Size(96,37);
            button.Padding=new Padding(7,0,7,0);
            button.Margin=new Padding(0,0,5,5);
            button.Click+=handler;
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
            overlay.RefreshQuickPanel();
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
#if DISTRIBUTION
            inspect.Enabled &= license != null && license.Allows("inspection");
            live.Enabled &= license != null && license.Allows("live");
            demo.Enabled &= license != null && license.Allows("demo");
#endif
            adbPath.Enabled=consolePath.Enabled=profilePath.Enabled=devices.Enabled=instances.Enabled=!value&&!running;
            connectionSetup.Enabled=profileRow.Enabled=!value&&!running;
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
                instances.ReplaceItems(new object[0]); devices.ReplaceItems(new object[0]);
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
                devices.ReplaceItems(found.ToArray());
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
            if (busy) return;
#if DISTRIBUTION
            using (var permission = license == null ? null : license.BeginWork("inspection"))
            {
                if (permission == null) return;
                await InspectWithPermission(permission.Token);
            }
#else
            await InspectWithPermission(CancellationToken.None);
#endif
        }
        private async Task InspectWithPermission(CancellationToken token)
        {
            SetBusy(true);
            try { Adb adb=SelectedAdb(); Profile profile=String.IsNullOrWhiteSpace(profilePath.Text)?null:Profile.Load(profilePath.Text); string text=await Task.Run(()=>Diagnostics.Run(adb,profile,token)); token.ThrowIfCancellationRequested(); WriteLog(text); SetStatus("Inspection complete / see results below"); }
            catch (OperationCanceledException) { SetStatus("Stopped"); }
            catch (Exception ex) { Failure(ex); }
            finally { SetBusy(false); }
        }
        private void StartDemo()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
#if DISTRIBUTION
            if (license == null || !license.Allows("demo")) return;
            if (demoPermission != null) demoPermission.Dispose();
            demoPermission = license.BeginWork("demo");
            if (demoPermission == null) return;
#endif
            demoMode=true; demoClock.Restart(); overlay.HideOverlay(); latest=Demo.Make(0); SetStatus("DEMO / generated actors only / no game data"); WriteLogText("Demo started. All displayed positions and labels are synthetic."); SetBusy(false);
        }
        internal async Task StartLive()
        {
            if (busy || (readerTask!=null&&!readerTask.IsCompleted)) return;
#if DISTRIBUTION
            if (license == null || !license.Allows("live")) return;
#endif
            SetBusy(true);
            try
            {
                Profile p=String.IsNullOrWhiteSpace(profilePath.Text)?null:Profile.Load(profilePath.Text); Adb adb=SelectedAdb();
                Instance target=instances.SelectedItem as Instance;
                if (target==null) throw new IOException(L.Format("Select the matching running {0} game window.",Emulators.Name(activeEmulator)));
                if (!Native.IsWindow(target.Window)) throw new IOException(L.Text("The game render surface is unavailable. Restore the emulator game window and refresh devices."));
                demoMode=false; latest=null; frameBuffer.Clear(); overlay.HideOverlay();
                if (readerCancel!=null) readerCancel.Dispose();
#if DISTRIBUTION
                if (demoPermission != null) { demoPermission.Dispose(); demoPermission = null; }
                readerCancel = license.BeginWork("live");
                if (readerCancel == null) return;
#else
                readerCancel = new CancellationTokenSource();
#endif
                CancellationToken token=readerCancel.Token;
                string package=p==null?Profile.GlobalPackage:p.Package;
                GameInfo info=await Task.Run(()=>GameInfo.Inspect(adb,package,false,token));
                if (p==null) p=Profile.ForInstalled(info);
                p.Validate();
                GameCompatibility.Ensure(p,info,Emulators.Name(activeEmulator));
                info.Maps=await Task.Run(()=>adb.ReadMaps(info.Pid,token));
                List<MapRegion> maps=MapRegion.Parse(info.Maps); ulong baseAddress=MapRegion.ModuleBase(maps,p.Module);
                AndroidMemory memory=new AndroidMemory(adb,info.Pid,maps,token);
                await Task.Run(()=>memory.Read(baseAddress,64));
                NativeReader nativeReader=null;
                if (p.Backend=="Native") { await Task.Run(()=>NativeReader.Prepare(adb,p,info,maps,token)); nativeReader=new NativeReader(adb,info.Pid,baseAddress,p); }
                token.ThrowIfCancellationRequested();
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
                        if (!closing && !IsDisposed) try { BeginInvoke(new Action(delegate { if(readerCancel==null||readerCancel.Token!=token)return;StopAimInput();overlay.HideOverlay(); SetStatus("Live reader stopped / see log"); SetBusy(false); })); } catch (InvalidOperationException) { }
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
#if DISTRIBUTION
            if (demoPermission != null) { demoPermission.Dispose(); demoPermission = null; }
#endif
            StopAimInput();
            liveDisplay=false; overlay.Suppressed=true; demoMode=false; latest=null; frameBuffer.Clear(); overlay.HideOverlay();
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
            bool arrows=game&&overlay.Quick.Open&&!overlay.Quick.Collapsed&&!overlay.Quick.Hidden;
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
#if DISTRIBUTION
            if (license == null || !license.Allows(null)) return;
#endif
            toggleHotkey=RegisterShortcut(1);stopHotkey=RegisterShortcut(2);menuHotkey=RegisterShortcut(3);UpdateQuickBindings();
        }
        private void CaptureShortcuts(bool capturing)
        {
            if(capturing) { ReleaseShortcuts();shortcutsSuspended=true; }
            else if(!closing)RebindShortcuts();
        }
        private void QueueQuickSave()
        {
            quickSavePending=true;
            quickSaveDue=DateTime.UtcNow.AddMilliseconds(300);
        }
        private void ChangedQuickOptions()
        {
            aim.Reset();QueueQuickSave();overlay.RefreshQuickPanel();scene.Invalidate();
        }
        private void ProcessGamePanelAction(QuickHudAction action)
        {
            if(!GameFocused())return;
            switch(action)
            {
                case QuickHudAction.Collapse: overlay.Quick.ToggleCollapse(options,overlay.Suppressed);break;
                case QuickHudAction.Hide: overlay.Quick.Hidden=true;break;
                case QuickHudAction.Adjustments: overlay.Quick.ToggleSection(QuickHudSection.Adjustments);break;
                case QuickHudAction.Display: overlay.Quick.ToggleSection(QuickHudSection.Display);break;
                case QuickHudAction.Settings: OpenMenu();return;
                case QuickHudAction.Aim:
                case QuickHudAction.Activation:
                case QuickHudAction.Target:
                case QuickHudAction.Esp:
                    QuickActions.Apply(options,3+(action-QuickHudAction.Aim));
                    ChangedQuickOptions();
                    return;
                default:
                    foreach(QuickHud.DisplayOption option in QuickHud.DisplayOptions)
                    {
                        if(option.Action!=action)continue;
                        option.Set(options,!option.Get(options));
                        ChangedQuickOptions();
                        return;
                    }
                    return;
            }
            UpdateQuickBindings();
            overlay.RefreshQuickPanel();
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
                if (m.WParam.ToInt32()==1) { overlay.Suppressed=!overlay.Suppressed;overlay.RefreshQuickPanel();WriteLogText(overlay.Suppressed?"Overlay hidden.":"Overlay enabled."); }
                else if (m.WParam.ToInt32()==2) { Task ignored=StopReader(); }
                else if (m.WParam.ToInt32()==3)
                {
                    if(GameFocused())ProcessGamePanelAction(QuickHudAction.Adjustments);
                    else OpenMenu();
                }
                else if(m.WParam.ToInt32()==8)OpenMenu();
                else if(m.WParam.ToInt32()==9)ProcessGamePanelAction(QuickHudAction.Collapse);
                else if(m.WParam.ToInt32()>=4&&m.WParam.ToInt32()<=7)ProcessGamePanelAction(QuickHudAction.Aim+(m.WParam.ToInt32()-4));
                else if(m.WParam.ToInt32()>=20&&m.WParam.ToInt32()<=23&&GameFocused()&&overlay.Quick.Open&&!overlay.Quick.Collapsed&&!overlay.Quick.Hidden)
                {
                    int id=m.WParam.ToInt32();
                    if(id==22)overlay.Quick.SelectedRow=(overlay.Quick.SelectedRow+2)%3;
                    else if(id==23)overlay.Quick.SelectedRow=(overlay.Quick.SelectedRow+1)%3;
                    else { QuickActions.Adjust(options,overlay.Quick.SelectedRow,id==20?-1:1);ChangedQuickOptions(); }
                    overlay.RefreshQuickPanel();
                }
            }
            base.WndProc(ref m);
        }
        private async void OnClosing(object sender,FormClosingEventArgs e)
        {
            if (closing) return;
            e.Cancel=true; closing=true; liveDisplay=false; overlay.Suppressed=true; overlay.HideOverlay(); timer.Stop();StopAimInput(); if (readerCancel!=null) readerCancel.Cancel();
#if DISTRIBUTION
            if (demoPermission != null) { demoPermission.Dispose(); demoPermission = null; }
#endif
            UpdateQuickBindings();
            ReleaseShortcuts();
            if (readerTask!=null) try { await readerTask; } catch { }
            if (toggleHotkey) Native.UnregisterHotKey(Handle,1); if (stopHotkey) Native.UnregisterHotKey(Handle,2); overlay.Dispose();
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
