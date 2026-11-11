using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ESPWorkbench
{
    public sealed class AimOptions
    {
        public bool Enabled, ShowRadius=true, Players=true, Training=true, RequireCapturedCursor;
        public int Strength=15, Radius=90, HoldKey=0x02;
        public int InputVersion;
        public string AimPoint="Chest", Activation="Hold", InputBackend="Auto";
        public float MaxDistance=150, InputScale=1;
        public void EnsureDefaults()
        {
            if(InputVersion<1) { RequireCapturedCursor=false;InputVersion=1; }
            Strength=Math.Max(0,Math.Min(100,Strength)); Radius=Math.Max(20,Math.Min(1200,Radius));
            MaxDistance=Vec3.Good(MaxDistance)?Math.Max(10,Math.Min(500,MaxDistance)):150;
            InputScale=Vec3.Good(InputScale)?Math.Max(.1f,Math.Min(5,InputScale)):1;
            if(HoldKey!=0x02&&HoldKey!=0x05&&HoldKey!=0x06&&HoldKey!=0x12) HoldKey=0x02;
            if(AimPoint!="Head"&&AimPoint!="Chest"&&AimPoint!="Legs")AimPoint="Chest";
            if(Activation=="InsideCircle")Activation="Always";
            if(Activation!="Hold"&&Activation!="Always"&&Activation!="Scoped"&&Activation!="Firing"&&Activation!="ScopedAndFiring")Activation="Hold";
            // Touchscreen gestures cancel GameLoop's own multi-touch controls. Retire that transport.
            if(InputBackend!="Auto"&&InputBackend!="WindowsMouse")InputBackend="Auto";
        }
        public float ScreenRadius(Size size) { return Radius*size.Height/1080f; }
        public float HeightFraction { get { return AimPoint=="Head"?.94f:AimPoint=="Legs"?.18f:.65f; } }
        public string PointLabel { get { return AimPoint=="Head"?"الرأس":AimPoint=="Legs"?"الأرجل":"الصدر"; } }
        public string KeyLabel { get { return HoldKey==0x05?"Mouse 4":HoldKey==0x06?"Mouse 5":HoldKey==0x12?"Alt":"RMB"; } }
    }
    public sealed class AimTarget
    {
        public string Address;
        public PointF Point;
        public double Error;
    }
    // Geometry and smoothing only. No game writes, hooks, or firing.
    public sealed class AimEngine
    {
        private string locked;
        private double remainderX,remainderY;
        public AimTarget Target { get; private set; }
        public void Reset() { locked=null; Target=null; remainderX=remainderY=0; }
        public static bool Fresh(Frame frame,DateTime now)
        {
            double age=frame==null?Double.PositiveInfinity:(now-frame.CapturedUtc).TotalMilliseconds;
            return frame!=null&&frame.CameraMode&&age>=0&&age<=100&&frame.Actors!=null&&frame.Origin.Finite&&Vec3.Good(frame.UnitsPerMeter)&&frame.UnitsPerMeter>0;
        }
        public static AimTarget Select(Frame frame,Size size,AimOptions options,string preferred)
        {
            if(size.Width<10||size.Height<10||!Fresh(frame,DateTime.UtcNow))return null;
            AimTarget best=null; double radius=options.ScreenRadius(size);
            foreach(Actor actor in frame.Actors)
            {
                if(actor==null||String.IsNullOrEmpty(actor.Address)||!actor.Position.Finite||!Vec3.Good(actor.Height)||actor.Height<=0||actor.Height>400)continue;
                if(actor.Kind=="Character") { if(!options.Players||frame.LocalTeam<0||actor.Team==frame.LocalTeam)continue; }
                else if(actor.Kind!="TrainingTarget"||!options.Training)continue;
                if(!Vec3.Good(actor.Health)||!Vec3.Good(actor.MaxHealth)||actor.Health<=0||actor.MaxHealth<=0||actor.Health>actor.MaxHealth*1.1f)continue;
                float distance=actor.Position.Distance(frame.Origin)/frame.UnitsPerMeter;
                if(!Vec3.Good(distance)||distance>options.MaxDistance||distance<1)continue;
                PointF point;
                // Aim regions estimated from feet/height, not skeleton bones.
                if(!frame.Project(new Vec3(actor.Position.X,actor.Position.Y,actor.Position.Z+actor.Height*options.HeightFraction),size,out point))continue;
                if(point.X<1||point.Y<1||point.X>=size.Width-1||point.Y>=size.Height-1)continue;
                double dx=point.X-size.Width/2.0,dy=point.Y-size.Height/2.0,error=Math.Sqrt(dx*dx+dy*dy);
                if(error>radius)continue;
                AimTarget candidate=new AimTarget { Address=actor.Address,Point=point,Error=error };
                if(actor.Address==preferred)return candidate;
                if(best==null||error<best.Error)best=candidate;
            }
            return best;
        }
        public Point Step(Frame frame,Size size,AimOptions options,double seconds,bool active)
        {
            options.EnsureDefaults();
            if(!active||!options.Enabled||options.Strength<=0||!Fresh(frame,DateTime.UtcNow)||!Vec3.Good((float)seconds)||seconds<=0||seconds>.1) { Reset(); return Point.Empty; }
            Target=Select(frame,size,options,locked);
            if(Target==null) { Reset(); return Point.Empty; }
            if(locked!=Target.Address)remainderX=remainderY=0;
            locked=Target.Address;
            double dx=Target.Point.X-size.Width/2.0,dy=Target.Point.Y-size.Height/2.0;
            if(Target.Error<=2.5*size.Height/1080.0) { remainderX=remainderY=0; return Point.Empty; }
            double dt=Math.Min(seconds,1.0/30),strength=options.Strength/100.0;
            double alpha=1-Math.Exp(-12*strength*dt);
            double x=dx*alpha*options.InputScale,y=dy*alpha*options.InputScale;
            double length=Math.Sqrt(x*x+y*y),limit=600*strength*dt;
            if(length>limit) { x*=limit/length; y*=limit/length; }
            x+=remainderX;y+=remainderY;
            int ix=(int)Math.Truncate(x),iy=(int)Math.Truncate(y);
            remainderX=x-ix;remainderY=y-iy;
            return new Point(ix,iy);
        }
    }
    internal sealed class AimInputState
    {
        public bool WindowValid,Foreground,KeyHeld,CursorKnown,CursorVisible;
        public string Error;
        public string BlockReason(AimOptions options)
        {
            if(Error!=null)return Error;
            if(!WindowValid)return "نافذة اللعبة غير متاحة";
            if(!Foreground)return "ارجع لنافذة اللعبة";
            if(options.Activation=="Hold"&&!KeyHeld)return "اضغط باستمرار على "+options.KeyLabel;
            if(options.RequireCapturedCursor&&(!CursorKnown||CursorVisible))return "فحص المؤشر أوقف الحركة — عطّله من الإعدادات";
            return null;
        }
    }
    internal interface IAimInput
    {
        AimInputState Read(IntPtr target,AimOptions options);
        bool Move(Point delta);
        void Pause();
        long Delivered { get; }
        string Description { get; }
    }
    internal sealed class MouseInput : IAimInput
    {
        private uint cachedPid;
        private int cachedIntegrity=-1;
        private long delivered;
        public long Delivered { get { return delivered; } }
        public string Description { get { return "Windows Mouse"; } }
        public void Pause() { }
        [StructLayout(LayoutKind.Sequential)] internal struct Mouse { public int X,Y; public uint Data,Flags,Time; public UIntPtr Extra; }
        // INPUT's union is 32 bytes on x64; the mouse member is its largest member.
        [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type; public Mouse Mouse; }
        [StructLayout(LayoutKind.Sequential)] private struct CursorInfo { public int Size,Flags; public IntPtr Cursor; public Native.Point Position; }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CursorInfo info);
        [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint count,Input[] inputs,int size);
        public AimInputState Read(IntPtr target,AimOptions options)
        {
            AimInputState state=new AimInputState { KeyHeld=(GetAsyncKeyState(options.HoldKey)&0x8000)!=0 };
            CursorInfo info=new CursorInfo { Size=Marshal.SizeOf(typeof(CursorInfo)) };
            state.CursorKnown=GetCursorInfo(ref info);state.CursorVisible=(info.Flags&1)!=0;
            if(target==IntPtr.Zero||!Native.IsWindow(target)||!Native.IsWindowVisible(target))return state;
            IntPtr root=Native.GetAncestor(target,2);
            state.WindowValid=root!=IntPtr.Zero&&!Native.IsIconic(root);
            state.Foreground=state.WindowValid&&Native.GetForegroundWindow()==root;
            uint pid;Native.GetWindowThreadProcessId(target,out pid);
            if(pid!=cachedPid) { cachedPid=pid;cachedIntegrity=WindowsInputAccess.Integrity((int)pid); }
            state.Error=WindowsInputAccess.BlockReason(WindowsInputAccess.CurrentIntegrity,cachedIntegrity);
            return state;
        }
        public bool Move(Point delta)
        {
            Input input=new Input { Mouse=new Mouse { X=delta.X,Y=delta.Y,Flags=0x0001|0x2000 } };
            bool sent=SendInput(1,new[]{input},Marshal.SizeOf(typeof(Input)))==1;if(sent)delivered++;return sent;
        }
        internal static int StructureSize { get { return Marshal.SizeOf(typeof(Input)); } }
    }
    public sealed class AimController
    {
        private readonly AimEngine engine=new AimEngine();
        private IAimInput input;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private Vec3 responseCamera;
        private bool haveResponseCamera;
        private double quietSeconds;
        private long responseStartMoves;
        public AimController() : this(new MouseInput()) { }
        internal AimController(IAimInput input) { this.input=input; }
        internal void SetInput(IAimInput next) { Reset();input=next; }
        public long DeliveredMovements { get { return input.Delivered; } }
        public string InputDescription { get { return input.Description; } }
        public AimTarget Target { get { return engine.Target; } }
        public AimTarget Candidate { get; private set; }
        internal Frame DecisionFrame { get; private set; }
        internal AimInputState InputState { get; private set; }
        public string State { get; private set; }
        public long SentMovements { get; private set; }
        public void Reset() { input.Pause();engine.Reset();Candidate=null;DecisionFrame=null;haveResponseCamera=false;quietSeconds=0;clock.Restart();State="متوقف"; }
        public bool Update(Frame frame,IntPtr target,AimOptions options,bool allowed)
        {
            double dt=clock.Elapsed.TotalSeconds;clock.Restart();
            return UpdateAt(frame,Native.ClientBounds(target).Size,target,options,allowed,dt);
        }
        internal bool UpdateAt(Frame frame,Size size,IntPtr target,AimOptions options,bool allowed,double dt)
        {
            DecisionFrame=frame;
            options.EnsureDefaults();InputState=input.Read(target,options);
            string blocked=!options.Enabled?"المساعدة معطلة":!allowed?"الجلسة أو القائمة توقف الحركة":InputState.BlockReason(options);
            if(blocked==null&&options.Strength==0)blocked="القوة صفر — ارفع شريط القوة";
            if(blocked==null&&!AimEngine.Fresh(frame,DateTime.UtcNow))blocked="بانتظار بيانات حديثة";
            if(blocked==null)blocked=ActivationBlock(options,frame);
            if(blocked==null&&(dt<=0||dt>.1||Double.IsNaN(dt)||Double.IsInfinity(dt)))blocked="تأخر التحديث — إيقاف الحركة مؤقتًا";
            Candidate=allowed&&options.Enabled?AimEngine.Select(frame,size,options,engine.Target==null?null:engine.Target.Address):null;
            Point movement=engine.Step(frame,size,options,dt,blocked==null);
            State=blocked??(engine.Target==null?"لا يوجد هدف صالح داخل الدائرة":movement==Point.Empty?"هدف محدد — تنعيم الحركة":"إرسال حركة التصويب");
            if(blocked!=null||engine.Target==null) { input.Pause();haveResponseCamera=false;quietSeconds=0; }
            else
            {
                double yaw=haveResponseCamera?Math.Abs((frame.CameraRotation.Y-responseCamera.Y+540)%360-180):0;
                if(!haveResponseCamera||Math.Abs(frame.CameraRotation.X-responseCamera.X)>.01||yaw>.01)
                {
                    responseCamera=frame.CameraRotation;responseStartMoves=SentMovements;quietSeconds=0;haveResponseCamera=true;
                }
                else quietSeconds+=dt;
                if(quietSeconds>.75&&SentMovements-responseStartMoves>=12)State="حركات مرسلة بلا استجابة كاميرا — راجع طريقة الإدخال";
            }
            if(engine.Target!=null&&engine.Target.Error<=2.5*size.Height/1080.0)input.Pause();
            if(movement==Point.Empty)return true;
            InputState=input.Read(target,options);
            if(!allowed||!AimEngine.Fresh(frame,DateTime.UtcNow)||InputState.BlockReason(options)!=null) { input.Pause();engine.Reset();State=InputState.BlockReason(options)??"بانتظار بيانات حديثة";return true; }
            if(input.Move(movement)) { SentMovements++;return true; }
            input.Pause();options.Enabled=false;engine.Reset();State="تعذر إرسال حركة الإدخال";return false;
        }
        internal static string ActivationBlock(AimOptions options,Frame frame)
        {
            if(options.Activation=="Scoped"||options.Activation=="ScopedAndFiring")
            {
                if(frame==null||!frame.LocalScoped.HasValue)return "حالة السكوب غير متاحة";
                if(frame.LocalScoped!=true)return "افتح السكوب لتفعيل المساعدة";
            }
            if(options.Activation=="Firing"||options.Activation=="ScopedAndFiring")
            {
                if(frame==null||!frame.LocalFiring.HasValue)return "حالة إطلاق النار غير متاحة";
                if(frame.LocalFiring!=true)return "اضرب لتفعيل المساعدة";
            }
            return null;
        }
    }
}
