using System;
using System.Drawing;
using System.Linq;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    internal sealed class FixtureAimInput : IAimInput
    {
        public AimInputState State=new AimInputState { WindowValid=true,Foreground=true,KeyHeld=true,CursorKnown=true,CursorVisible=true };
        public int Sent,Reads;
        public bool Accept=true,LoseFocusOnRecheck;
        public int Pauses;
        public long Delivered { get { return Sent; } }
        public string Description { get { return "Fixture"; } }
        public void Pause() { Pauses++; }
        public AimInputState Read(IntPtr window,AimOptions options)
        {
            Reads++;if(LoseFocusOnRecheck&&Reads>=2)State.Foreground=false;return State;
        }
        public bool Move(Point delta) { if(Accept)Sent++;return Accept; }
    }
    internal static class AimTests
    {
        private static Actor Enemy(string id,float y,int team=2,string kind="Character")
        {
            return new Actor { Address=id,Kind=kind,Team=team,Position=new Vec3(10000,y,-117),Height=180,Health=100,MaxHealth=100 };
        }
        private static Frame Scene(params Actor[] actors)
        {
            return new Frame { CameraMode=true,Fov=90,LocalTeam=1,Actors=actors };
        }
        public static void Run(Action<bool,string> check)
        {
            VisibilityTests.Run(check);
            Size size=new Size(1920,1080);AimOptions o=new AimOptions { Enabled=true };
            Frame f=Scene(Enemy("ally",0,1),Enemy("far",800),Enemy("near",400),Enemy("car",100,2,"Vehicle"));
            check(AimEngine.Select(f,size,o,null).Address=="near","aim chooses nearest enemy to screen centre and excludes allies/non-characters");
            check(AimEngine.Select(f,size,o,"far").Address=="far","aim retains valid target instead of switching each frame");
            o.Radius=20;check(AimEngine.Select(f,size,o,null)==null,"aim ignores targets outside assistance circle");o.Radius=90;
            o.MaxDistance=50;check(AimEngine.Select(f,size,o,null)==null,"aim respects distance limit");o.MaxDistance=150;
            Frame bad=Scene(Enemy("dead",100));bad.Actors[0].Health=0;
            check(AimEngine.Select(bad,size,o,null)==null,"aim excludes dead targets");
            bad.Actors[0].Health=float.NaN;check(AimEngine.Select(bad,size,o,null)==null,"aim rejects invalid target health");
            bad=Scene(Enemy("invalid",100));bad.Actors[0].Position.X=float.NaN;
            check(AimEngine.Select(bad,size,o,null)==null,"aim rejects invalid positions");
            bad=Scene(Enemy("behind",100));bad.Actors[0].Position.X=-10000;
            check(AimEngine.Select(bad,size,o,null)==null,"aim excludes targets behind camera");
            bad=Scene(Enemy("unknownTeam",100));bad.LocalTeam=-1;
            check(AimEngine.Select(bad,size,o,null)==null,"aim skips characters when local team is unknown");
            bad=Scene(Enemy("training",100,0,"TrainingTarget"));o.Players=false;
            check(AimEngine.Select(bad,size,o,null)!=null,"training aim independent of player setting");o.Training=false;
            check(AimEngine.Select(bad,size,o,null)==null,"training aim can be disabled independently");o.Players=o.Training=true;
            AimEngine engine=new AimEngine();f=Scene(Enemy("near",600));
            check(engine.Step(f,size,o,.033,true).X>0,"aim correction moves toward projected target");
            check(engine.Step(f,size,o,.016,false)==Point.Empty&&engine.Target==null,"releasing activation clears motion and target");
            o.Enabled=false;check(engine.Step(f,size,o,.033,true)==Point.Empty,"disabled aim never generates input");o.Enabled=true;
            o.Strength=0;check(engine.Step(f,size,o,.033,true)==Point.Empty,"zero strength never generates input");o.Strength=15;
            f.CapturedUtc=DateTime.UtcNow.AddMilliseconds(-150);check(engine.Step(f,size,o,.033,true)==Point.Empty,"aim rejects data older than 100 ms");
            f.CapturedUtc=DateTime.UtcNow.AddSeconds(1);check(engine.Step(f,size,o,.033,true)==Point.Empty,"aim rejects future timestamps");
            f.CapturedUtc=DateTime.UtcNow;f.CameraMode=false;check(engine.Step(f,size,o,.033,true)==Point.Empty,"synthetic matrix preview cannot drive aim");f.CameraMode=true;
            check(engine.Step(f,size,o,.2,true)==Point.Empty,"long UI stalls cannot cause a catch-up jump");
            check(engine.Step(f,size,o,double.NaN,true)==Point.Empty,"invalid elapsed time cannot produce input");
            f=Scene(Enemy("centre",0));check(engine.Step(f,size,o,.033,true)==Point.Empty,"dead zone avoids centre jitter");
            f=Scene(Enemy("near",600));double low=0,high=0;
            for(int i=0;i<60;i++) {f.CapturedUtc=DateTime.UtcNow;low+=engine.Step(f,size,o,1.0/60,true).X;}
            engine.Reset();o.Strength=60;
            for(int i=0;i<60;i++) {f.CapturedUtc=DateTime.UtcNow;high+=engine.Step(f,size,o,1.0/60,true).X;}
            check(low>0&&low<=91&&high>low&&high<=361,"strength controls total correction with a bounded count rate");
            o.Strength=15;engine.Reset();double at120=0;
            for(int i=0;i<120;i++) {f.CapturedUtc=DateTime.UtcNow;at120+=engine.Step(f,size,o,1.0/120,true).X;}
            check(Math.Abs(at120-low)<=1,"aim speed independent of 60 vs 120 Hz timer");
            // Closed-loop fixture: an independent camera model responds to mouse counts.
            // Assert convergence rather than duplicating the smoothing formula.
            engine.Reset();f=Scene(Enemy("feedback",600));double initial=AimEngine.Select(f,size,o,null).Error;
            for(int i=0;i<600;i++)
            {
                f.CapturedUtc=DateTime.UtcNow;Point move=engine.Step(f,size,o,1.0/120,true);
                f.CameraRotation.Y+=move.X*.04f;f.CameraRotation.X-=move.Y*.04f;
            }
            f.CapturedUtc=DateTime.UtcNow;AimTarget settled=AimEngine.Select(f,size,o,null);
            check(settled!=null&&settled.Error<initial*.2&&Math.Abs(f.CameraRotation.Y)<5,"gentle aim converges with camera feedback without a large turn");
            f=Scene(Enemy("gone",600));engine.Step(f,size,o,.016,true);f.Actors=new Actor[0];
            check(engine.Step(f,size,o,.016,true)==Point.Empty&&engine.Target==null,"despawn clears target and fractional movement immediately");
            check(Math.Abs(o.ScreenRadius(new Size(1280,720))-60)<.01,"aim radius scales with viewport height");
            o.Strength=999;o.Radius=-1;o.InputScale=float.NaN;o.HoldKey=1;o.MaxDistance=float.PositiveInfinity;o.EnsureDefaults();
            check(o.Strength==100&&o.Radius==20&&o.InputScale==1&&o.HoldKey==2&&o.MaxDistance==150,"corrupt aim settings normalized");
            RenderOptions legacy=new JavaScriptSerializer().Deserialize<RenderOptions>("{\"Boxes\":false}");legacy.EnsureDefaults();
            check(!legacy.Aim.Enabled&&legacy.Aim.Strength==15,"legacy settings receive disabled gentle aim defaults");
            legacy.Aim.Enabled=true;legacy.Aim.Strength=23;legacy.Aim.HoldKey=0x05;
            RenderOptions saved=new JavaScriptSerializer().Deserialize<RenderOptions>(new JavaScriptSerializer().Serialize(legacy));saved.EnsureDefaults();
            check(saved.Aim.Enabled&&saved.Aim.Strength==23&&saved.Aim.HoldKey==0x05,"aim settings persist through JSON round-trip");
            check(MouseInput.StructureSize==40,"x64 SendInput structure matches Windows ABI");
            check(!new MouseInput().Read(IntPtr.Zero,o).WindowValid,"missing target window cannot receive aim input");
            AimOptions upgraded=new JavaScriptSerializer().Deserialize<AimOptions>("{\"RequireCapturedCursor\":true,\"Strength\":37}");upgraded.EnsureDefaults();
            check(!upgraded.RequireCapturedCursor&&upgraded.Strength==37&&upgraded.InputVersion==1,"legacy cursor guard migrates without changing saved strength");
            upgraded.RequireCapturedCursor=true;upgraded.EnsureDefaults();
            check(upgraded.RequireCapturedCursor,"explicit optional cursor guard persists after migration");
            f=Scene(Enemy("region",100));o=new AimOptions { Enabled=true,Radius=300 };o.EnsureDefaults();
            o.AimPoint="Head";float head=AimEngine.Select(f,size,o,null).Point.Y;
            o.AimPoint="Chest";float chest=AimEngine.Select(f,size,o,null).Point.Y;
            o.AimPoint="Legs";float legs=AimEngine.Select(f,size,o,null).Point.Y;
            check(head<chest&&chest<legs,"selected head chest and leg regions project in correct order");
            o.AimPoint="Chest";f=Scene(Enemy("wide",2500));o.Radius=90;
            check(AimEngine.Select(f,size,o,null)==null,"small circle excludes peripheral target");o.Radius=300;
            check(AimEngine.Select(f,size,o,null)!=null,"enlarging circle admits peripheral target");
            o.Radius=1200;o.EnsureDefaults();check(o.Radius==1200,"wide assistance circle supported up to 1200 pixels at 1080p");
            o.AimPoint="invalid";o.Activation="invalid";o.EnsureDefaults();
            check(o.AimPoint=="Chest"&&o.Activation=="Hold","invalid aim region and activation normalize");
            o.AimPoint="Head";o.Activation="Always";
            AimOptions roundTrip=new JavaScriptSerializer().Deserialize<AimOptions>(new JavaScriptSerializer().Serialize(o));roundTrip.EnsureDefaults();
            check(roundTrip.AimPoint=="Head"&&roundTrip.Activation=="Always"&&roundTrip.Radius==1200,"region circle size and activation persist");
            o=new AimOptions { Enabled=true };o.EnsureDefaults();f=Scene(Enemy("input",600));
            FixtureAimInput input=new FixtureAimInput();AimController controller=new AimController(input);
            controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent>0&&controller.SentMovements>0,"visible GameLoop cursor does not block default mouse input");
            int sent=input.Sent;o.RequireCapturedCursor=true;
            controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.State==L.Text("فحص المؤشر أوقف الحركة — عطّله من الإعدادات"),"optional cursor guard reports its exact blocking reason");o.RequireCapturedCursor=false;
            input.State.KeyHeld=false;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.State==L.Format("اضغط باستمرار على {0}",L.Text(o.KeyLabel)),"hold mode reports missing activation key");
            o.Activation="Always";controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent>sent,"inside-circle mode sends input without a held activation key");sent=input.Sent;
            input.State.Foreground=false;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.Target==null,"foreground loss blocks and resets continuous input");input.State.Foreground=true;
            controller.UpdateAt(f,size,IntPtr.Zero,o,false,.033);
            check(input.Sent==sent&&controller.Candidate==null,"menu stop and hidden-overlay gate block continuous input");
            input.Reads=0;input.LoseFocusOnRecheck=true;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.Target==null,"focus recheck prevents sending after foreground changes");
            input.LoseFocusOnRecheck=false;input.State.Foreground=true;input.Accept=false;
            check(!controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033)&&!o.Enabled,"Windows input rejection disables aim and reports failure");
            o.Enabled=true;input.Accept=true;input.Sent=0;controller.Reset();
            for(int i=0;i<35;i++) { f.CapturedUtc=DateTime.UtcNow;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033); }
            check(controller.State==L.Text("حركات مرسلة بلا استجابة كاميرا — راجع طريقة الإدخال"),"accepted input without camera response gets a distinct diagnostic");
            f.CameraRotation.Y+=.5f;f.CapturedUtc=DateTime.UtcNow;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(controller.State!=L.Text("حركات مرسلة بلا استجابة كاميرا — راجع طريقة الإدخال"),"camera motion clears missing-response diagnostic");
            o.Strength=0;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(controller.State==L.Text("القوة صفر — ارفع شريط القوة"),"zero strength is reported instead of claiming no target");
            o.InputBackend="bad";o.EnsureDefaults();check(o.InputBackend=="Auto","unknown input transport defaults to automatic emulator selection");
            o.InputBackend="AndroidTouch";
            AimOptions transportSaved=new JavaScriptSerializer().Deserialize<AimOptions>(new JavaScriptSerializer().Serialize(o));transportSaved.EnsureDefaults();
            check(transportSaved.InputBackend=="Auto","retired Android touch transport migrates to non-interfering mouse input");
            o.Strength=15;o.Enabled=true;input.State.KeyHeld=true;int pauses=input.Pauses;
            controller.UpdateAt(f,size,IntPtr.Zero,o,false,.033);
            check(input.Pauses>pauses,"stop/menu gates release the input transport as well as resetting target selection");
            input.State.Error="Touch connection lost";controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(controller.State=="Touch connection lost","device transport error is exposed as the blocking reason");
            check(WindowsInputAccess.BlockReason(0x2000,0x3000)!=null,"medium integrity cannot silently send input to a high-integrity emulator");
            check(WindowsInputAccess.BlockReason(0x3000,0x3000)==null,"matching administrator integrity permits ordinary mouse input");
            check(WindowsInputAccess.BlockReason(-1,0x3000)!=null,"unknown integrity fails closed instead of claiming successful input");
            Frame activationFrame=new Frame { LocalScoped=false,LocalFiring=false };
            o.Activation="Scoped";check(AimController.ActivationBlock(o,activationFrame)!=null,"scope-only aim is blocked while scope is closed");
            activationFrame.LocalScoped=true;check(AimController.ActivationBlock(o,activationFrame)==null,"scope-only aim follows actual ADS state without a held right button");
            o.Activation="Firing";check(AimController.ActivationBlock(o,activationFrame)!=null,"fire-only aim is blocked while the weapon is idle");
            activationFrame.LocalFiring=true;check(AimController.ActivationBlock(o,activationFrame)==null,"fire-only aim follows actual weapon firing state");
            o.Activation="ScopedAndFiring";check(AimController.ActivationBlock(o,activationFrame)==null,"combined activation accepts scope and firing together");
            activationFrame.LocalScoped=false;check(AimController.ActivationBlock(o,activationFrame)!=null,"combined activation rejects hip firing");
            activationFrame.LocalScoped=null;o.Activation="Scoped";check(AimController.ActivationBlock(o,activationFrame)!=null,"unverified scope data cannot activate scope-only aim");
            activationFrame.LocalFiring=null;o.Activation="Firing";check(AimController.ActivationBlock(o,activationFrame)!=null,"unverified firing data cannot activate fire-only aim");
            o.Activation="Always";check(AimController.ActivationBlock(o,activationFrame)==null,"always mode does not require a scope or firing state");
            o.Activation="InsideCircle";o.EnsureDefaults();check(o.Activation=="Always","legacy continuous activation migrates to always mode");
            NativeSnapshot activationSnapshot=new NativeSnapshot { Actors=new NativeActorData[0],LocalScoped=true,LocalFiring=false };
            Frame activationConverted=activationSnapshot.ToFrame();check(activationConverted.LocalScoped==true&&activationConverted.LocalFiring==false,"reader snapshot carries actual activation flags into live frames");
            input.State.Error=null;input.State.KeyHeld=false;input.State.Foreground=true;input.Accept=true;
            f=Scene(Enemy("trigger",600));f.LocalScoped=true;f.LocalFiring=false;
            o.Enabled=true;o.Strength=15;o.Activation="Scoped";
            sent=input.Sent;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent>sent,"scope trigger sends correction from actual ADS even after the right button is released");
            sent=input.Sent;f.LocalScoped=false;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.Target==null,"closing the actual scope immediately stops scope-only corrections");
            o.Activation="Firing";controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent,"idle weapon does not receive fire-only corrections");
            f.LocalFiring=true;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent>sent,"actual weapon firing starts fire-only correction");
            sent=input.Sent;f.LocalFiring=false;controller.UpdateAt(f,size,IntPtr.Zero,o,true,.033);
            check(input.Sent==sent&&controller.Target==null,"ending weapon firing immediately stops fire-only correction");
            FrameBuffer stateBuffer=new FrameBuffer();DateTime stateTime=DateTime.UtcNow;
            stateBuffer.Push(new Frame { CameraMode=true,CapturedUtc=stateTime,LocalScoped=false,LocalFiring=false });
            stateBuffer.Push(new Frame { CameraMode=true,CapturedUtc=stateTime.AddMilliseconds(20),LocalScoped=true,LocalFiring=true });
            Frame sampledState=stateBuffer.SampleAt(stateTime.AddMilliseconds(10));
            check(sampledState.LocalScoped==true&&sampledState.LocalFiring==true,"display smoothing preserves the latest discrete scope and firing states");
        }
    }
}
