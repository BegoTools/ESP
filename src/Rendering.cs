using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ESPWorkbench
{
    public static class Demo
    {
        public static Frame Make(double time)
        {
            // A deterministic synthetic scene: forward +Y, right +X, up +Z.
            float[] matrix = new float[16];
            matrix[0] = 1; matrix[6] = 1; matrix[7] = 1; matrix[9] = 1.777778f;
            matrix[13] = -160*1.777778f; matrix[14] = -10;
            return new Frame { Source = "DEMO / synthetic data", Matrix = matrix, Origin = new Vec3(0,0,160), LocalTeam = 1, Actors = new[] {
                new Actor { Name="DEMO Alpha", Position=new Vec3(-800+(float)Math.Sin(time)*80,1250,0),Health=86,MaxHealth=100,Height=180,Team=2 },
                new Actor { Name="DEMO Bravo", Position=new Vec3(-300+(float)Math.Cos(time*.7)*60,1800,0),Health=42,MaxHealth=100,Height=180,Team=2 },
                new Actor { Name="DEMO Charlie", Position=new Vec3(850,2400,0),Health=100,MaxHealth=100,Height=180,Team=1 },
                new Actor { Name="DEMO Delta", Position=new Vec3(1900,3000,0),Health=65,MaxHealth=100,Height=180,Team=3 }
            }};
        }
    }
    public static class Painter
    {
        public static readonly Color Accent = Color.FromArgb(50,220,186);
        public static readonly Color KnockedColor=Color.FromArgb(255,55,70);
        public static Color ActorColor(Actor actor,RenderOptions options,bool ally)
        {
            bool character=String.IsNullOrEmpty(actor.Kind)||actor.Kind=="Character"||actor.Kind=="TrainingTarget";
            VisibilityOptions visibility=options.Visibility;
            if(character&&visibility!=null&&visibility.Enabled)
                return Color.FromArgb(actor.IsVisible==true?visibility.VisibleColorArgb:actor.IsVisible==false?visibility.OccludedColorArgb:visibility.UnknownColorArgb);
            return ally?Color.FromArgb(106,168,255):Color.FromArgb(options.Category(actor.Kind).ColorArgb);
        }
        public static bool Fresh(Frame frame) { return frame != null && (DateTime.UtcNow-frame.CapturedUtc).TotalSeconds < 2; }
        public static void Draw(Graphics g, Size size, Frame frame, RenderOptions options, bool demoBackground,bool showStatus=true)
        {
            bool smooth=demoBackground||options.ImageQualityEnabled;
            g.SmoothingMode = smooth?SmoothingMode.AntiAlias:SmoothingMode.None;
            g.TextRenderingHint=smooth?TextRenderingHint.AntiAliasGridFit:TextRenderingHint.SingleBitPerPixelGridFit;
            if (demoBackground)
            {
                g.Clear(Color.FromArgb(14,22,33));
                using (Pen grid = new Pen(Color.FromArgb(26,40,55)))
                {
                    for (int x=0;x<size.Width;x+=40) g.DrawLine(grid,x,0,x,size.Height);
                    for (int y=0;y<size.Height;y+=40) g.DrawLine(grid,0,y,size.Width,y);
                }
                using (Font f = new Font("Segoe UI",10))
                    g.DrawString(frame==null?L.Text("PREVIEW / Waiting for data"):frame.CameraMode?L.Text("LIVE PREVIEW / Game data"):L.Text("SYNTHETIC SCENE / No game data"), f, Brushes.LightSlateGray, 16, size.Height-32);
            }
            using (Font label = new Font("Segoe UI",options.FontSize,FontStyle.Bold))
            using (Font small = new Font("Segoe UI",8))
            {
                if (!Fresh(frame))
                {
                    Text(g,L.Text("WAITING FOR FRESH DATA"), label, Accent,16,16); return;
                }
                if(showStatus)
                {
                    Text(g,L.Text(frame.Source),label,Accent,16,16);
                    Text(g,L.Format("{0} entities  /  {1} actors  /  {2:0} Hz", frame.Actors.Length,frame.TotalActorCount,frame.ReadHertz),small,Color.Silver,16,37);
                }
                if(!options.EspEnabled) { if(demoBackground)Text(g,L.Text("ESP OFF"),label,Accent,16,60);return; }
                List<RectangleF> labels=new List<RectangleF>();
                Dictionary<int,EdgeContact> warnings=new Dictionary<int,EdgeContact>();
                Dictionary<string,int> drawn=new Dictionary<string,int>();
                foreach (Actor actor in frame.Actors.OrderBy(a=>a.Position.Distance(frame.Origin)))
                {
                    if(!options.Include(actor))continue;
                    CategoryOptions category=options.Category(actor.Kind);
                    bool character=String.IsNullOrEmpty(actor.Kind)||actor.Kind=="Character"||actor.Kind=="TrainingTarget";
                    if(character&&!CharacterLife.Retain(actor.Health,actor.MaxHealth,actor.IsDead,actor.NearDeathBreath))continue;
                    bool ally = character&&frame.LocalTeam >= 0 && actor.Team == frame.LocalTeam;
                    if (ally && !options.Allies) continue;
                    float distance = actor.Position.Distance(frame.Origin)/frame.UnitsPerMeter;
                    if (!Vec3.Good(distance) || distance > Math.Min(options.MaxDistance,category.MaxDistance)) continue;
                    if (options.Alerts&&category.Alerts&&!ally)
                    {
                        EdgeContact contact=Threats.Contact(new Vec3(actor.Position.X,actor.Position.Y,actor.Position.Z+actor.Height/2),frame,size);
                        if (contact!=null)
                        {
                            EdgeContact existing;
                            if (warnings.TryGetValue(contact.Sector,out existing))
                            {
                                int total=existing.Count+1;
                                if (contact.Distance<existing.Distance) { contact.Count=total; warnings[contact.Sector]=contact; } else existing.Count=total;
                            }
                            else warnings.Add(contact.Sector,contact);
                        }
                    }
                    string kind=actor.Kind??"Character"; int count; drawn.TryGetValue(kind,out count); if(count>=category.MaxVisible)continue;
                    PointF feet, head;
                    if (!frame.Project(actor.Position,size,out feet))continue;
                    if(character) { if(!frame.Project(new Vec3(actor.Position.X,actor.Position.Y,actor.Position.Z+actor.Height),size,out head))continue; } else head=new PointF(feet.X,feet.Y-12);
                    float height = Math.Abs(feet.Y-head.Y);
                    if (height < 3) continue;
                    float width = character?height*.46f:12, left = (head.X+feet.X)/2-width/2;
                    RectangleF box=RectangleF.Intersect(new RectangleF(left,Math.Min(head.Y,feet.Y),width,height),new RectangleF(1,1,size.Width-2,size.Height-2));
                    if (box.Width<=0||box.Height<=0) continue;
                    drawn[kind]=count+1;
                    PointF lineEnd=new PointF(Math.Max(1,Math.Min(size.Width-1,head.X)),Math.Max(1,Math.Min(size.Height-1,head.Y)));
                    Color color = ActorColor(actor,options,ally);
                    Color lineColor=actor.IsKnocked?KnockedColor:color;
                    if(options.Lines&&category.Lines)using(Pen line=new Pen(lineColor,options.LineWidth))g.DrawLine(line,size.Width/2f,3,lineEnd.X,lineEnd.Y);
                    using (Pen pen = new Pen(color,options.LineWidth))
                    {
                        if (options.Boxes&&category.Boxes) g.DrawRectangle(pen,box.X,box.Y,box.Width,box.Height);
                        else if(!character)g.DrawEllipse(pen,feet.X-3,feet.Y-3,6,6);
                    }
                    bool showHealth=options.Health&&category.Health&&actor.MaxHealth>0&&Vec3.Good(actor.Health);
                    if (showHealth&&character)
                    {
                        float ratio = Math.Max(0,Math.Min(1,actor.Health/actor.MaxHealth));
                        float healthX=Math.Max(1,box.Left-7);
                        using (Brush bg = new SolidBrush(Color.FromArgb(35,40,48))) g.FillRectangle(bg,healthX,box.Top,3,box.Height);
                        using (Brush hp = new SolidBrush(ratio < .35f ? Color.Salmon : color)) g.FillRectangle(hp,healthX,box.Bottom-box.Height*ratio,3,box.Height*ratio);
                    }
                    string text=options.Names&&category.Names?actor.Name:"";
                    if(actor.Kind=="Character"&&options.BotLabels)text=(actor.IsBot==true?L.Text("BOT"):actor.IsBot==false?L.Text("PLAYER"):L.Text("UNKNOWN"))+(text.Length>0?" | "+text:"");
                    if(actor.IsKnocked)text=L.Text("KNOCKED")+(text.Length>0?" | "+text:"");
                    if(actor.Kind=="Loot"&&options.Quantities&&actor.Quantity>1)text+=" x"+actor.Quantity;
                    if(actor.Kind=="Loot"&&options.ShowItemIds&&!String.IsNullOrEmpty(actor.ItemId))text+=" #"+actor.ItemId;
                    if (options.Distance&&category.Distance) text+=(text.Length>0?"  |  ":"")+distance.ToString("0",L.Culture)+" "+L.Text("m");
                    if(showHealth&&!character)text+=" | "+L.Text("HP")+" " +Math.Max(0,Math.Min(100,100*actor.Health/actor.MaxHealth)).ToString("0")+"%";
                    if(actor.Kind=="Vehicle"&&options.Fuel&&actor.HasFuel&&actor.MaxFuel>0)text+=" | "+L.Text("FUEL")+" " +Math.Max(0,Math.Min(100,100*actor.Fuel/actor.MaxFuel)).ToString("0")+"%";
                    if (text.Length>0&&labels.Count<options.LabelLimit)
                    {
                        SizeF measured=g.MeasureString(text,label); float labelWidth=measured.Width+8,labelHeight=measured.Height+4;
                        float labelX=Math.Max(2,Math.Min(size.Width-labelWidth-2,head.X-labelWidth/2));
                        RectangleF area=new RectangleF(labelX,Math.Max(58,Math.Min(size.Height-labelHeight-4,head.Y-labelHeight-4)),labelWidth,labelHeight);
                        for (int attempt=0;attempt<16&&labels.Exists(r=>r.IntersectsWith(area));attempt++)
                        { area.Y=Math.Max(58,area.Y-labelHeight-3); if (attempt>7) area.X=Math.Min(size.Width-labelWidth-2,area.X+labelWidth+5); }
                        labels.Add(area);
                        if (Math.Abs(area.Bottom-head.Y)>12) using (Pen leader=new Pen(Color.FromArgb(color.R/2,color.G/2,color.B/2),1)) g.DrawLine(leader,lineEnd.X,lineEnd.Y,area.Left+area.Width/2,area.Bottom);
                        using (Brush bg=new SolidBrush(Color.FromArgb(12,20,29))) g.FillRectangle(bg,area);
                        Text(g,text,label,actor.IsKnocked?KnockedColor:color,area.Left+4,area.Top+2);
                    }
                }
                foreach (EdgeContact contact in warnings.Values) DrawWarning(g,size,contact,label);
                if(options.PlayerCountVisible)DrawPlayerCount(g,size,NearbyPlayers.Count(frame,options));
            }
        }
        private static void DrawPlayerCount(Graphics g,Size size,int count)
        {
            float width=Math.Min(196,size.Width-24);
            if(width<100||size.Height<44)return;
            RectangleF badge=new RectangleF((size.Width-width)/2,10,width,30);
            using(GraphicsPath shape=SpectraTheme.Round(badge,8))
            using(Brush background=new LinearGradientBrush(badge,Color.FromArgb(68,33,95),Color.FromArgb(36,21,47),90))
            using(Pen border=new Pen(SpectraTheme.Accent,1))
            { g.FillPath(background,shape);g.DrawPath(border,shape); }
            using(Font label=new Font("Segoe UI",13,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Font number=new Font("Segoe UI",18,FontStyle.Bold,GraphicsUnit.Pixel))
            using(Brush foreground=new SolidBrush(Color.FromArgb(226,200,247)))
            using(Brush highlight=new SolidBrush(Color.FromArgb(231,255,105)))
            using(StringFormat rtl=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=(L.Rtl?StringFormatFlags.DirectionRightToLeft:0)|StringFormatFlags.NoWrap })
            using(StringFormat digits=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap })
            {
                g.DrawString(L.Text("اللاعبون حولك"),label,foreground,new RectangleF(badge.X+49,badge.Y,width-55,badge.Height),rtl);
                g.DrawString(count.ToString(),number,highlight,new RectangleF(badge.X+5,badge.Y,44,badge.Height),digits);
            }
        }
        private static void DrawWarning(Graphics g,Size size,EdgeContact contact,Font font)
        {
            double dx=Math.Sin(contact.Bearing),dy=-Math.Cos(contact.Bearing);
            double rx=Math.Max(20,size.Width/2.0-65),ry=Math.Max(20,size.Height/2.0-70);
            double scale=Math.Min(rx/Math.Max(.0001,Math.Abs(dx)),ry/Math.Max(.0001,Math.Abs(dy)));
            float x=(float)(size.Width/2.0+dx*scale),y=(float)(size.Height/2.0+dy*scale);
            PointF tip=new PointF(x+(float)dx*15,y+(float)dy*15);
            PointF a=new PointF(x-(float)dx*9+(float)dy*9,y-(float)dy*9-(float)dx*9);
            PointF b=new PointF(x-(float)dx*9-(float)dy*9,y-(float)dy*9+(float)dx*9);
            Color red=Color.FromArgb(255,82,91);
            using (Brush brush=new SolidBrush(red)) g.FillPolygon(brush,new[] {tip,a,b});
            string message=L.Text(contact.Direction)+"  "+contact.Distance.ToString("0",L.Culture)+" "+L.Text("m")+(contact.Count>1?"  +"+(contact.Count-1):"");
            SizeF measured=g.MeasureString(message,font);
            float lx=Math.Max(5,Math.Min(size.Width-measured.Width-13,x-(float)dx*28-measured.Width/2));
            float ly=Math.Max(58,Math.Min(size.Height-measured.Height-8,y-(float)dy*30-measured.Height/2));
            using (Brush background=new SolidBrush(Color.FromArgb(45,13,20))) g.FillRectangle(background,lx-4,ly-2,measured.Width+8,measured.Height+4);
            Text(g,message,font,red,lx,ly);
        }
        private static void CenterText(Graphics g,string text,Font font,Color c,float x,float y) { Text(g,text,font,c,x-g.MeasureString(text,font).Width/2,y); }
        private static void Text(Graphics g,string text,Font font,Color c,float x,float y)
        {
            g.DrawString(text,font,Brushes.Black,x+1,y+1);
            using (Brush b = new SolidBrush(c)) g.DrawString(text,font,b,x,y);
        }
    }
    public sealed class ScenePanel : Panel
    {
        public Frame Frame;
        public RenderOptions Options;
        public ScenePanel(RenderOptions options) { Options = options; DoubleBuffered = true; BackColor = Color.FromArgb(14,22,33); }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Painter.Draw(e.Graphics,ClientSize,Frame,Options,true); }
    }
    public static class Native
    {
        public delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int maximum);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int maximum);
        public static string WindowText(IntPtr window) { System.Text.StringBuilder text=new System.Text.StringBuilder(512); GetWindowText(window,text,text.Capacity); return text.ToString(); }
        public static string WindowClass(IntPtr window) { System.Text.StringBuilder text=new System.Text.StringBuilder(256); GetClassName(window,text,text.Capacity); return text.ToString(); }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left,Top,Right,Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h,out Rect r);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h,ref Point p);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int command);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flag);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
        public static Rectangle ClientBounds(IntPtr h)
        {
            Rect r; Point p = new Point();
            if (!GetClientRect(h,out r) || !ClientToScreen(h,ref p)) return Rectangle.Empty;
            return new Rectangle(p.X,p.Y,r.Right-r.Left,r.Bottom-r.Top);
        }
    }
    public sealed class Overlay : Form
    {
        private readonly AlphaSurface surface=new AlphaSurface();
        private bool alphaMode;
        public readonly QuickHudState Quick=new QuickHudState();
        public string AimState="off";
        public AimTarget AimCandidate;
        public long AimMovements;
        public Frame Frame;
        public readonly RenderOptions Options;
        public IntPtr Target;
        public bool Suppressed;
        public Overlay(RenderOptions options)
        {
            Text=L.ProductName+" "+L.Text("Overlay");
            Options = options; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            TopMost = true; BackColor=Color.Fuchsia;TransparencyKey=Color.Fuchsia;DoubleBuffered = true;
            StartPosition = FormStartPosition.Manual;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams p = base.CreateParams; p.ExStyle |= 0x80000|0x20|0x80|0x08000000; return p; } }
        public void UpdateFrame(Frame frame)
        {
            Frame = frame;
            bool valid = (!Suppressed||Quick.Open) && Target != IntPtr.Zero && Native.IsWindow(Target) && Native.IsWindowVisible(Target) && !Native.IsIconic(Native.GetAncestor(Target,2)) && Painter.Fresh(frame);
            IntPtr root = Native.GetAncestor(Target,2);
            if (!valid || Native.GetForegroundWindow() != root) { Hide(); return; }
            Rectangle bounds = Native.ClientBounds(Target);
            if (bounds.Width < 10 || bounds.Height < 10) { Hide(); return; }
            if (Bounds != bounds) Bounds = bounds;
            EnsurePresentationMode();
            if (!Visible) Show();
            if(alphaMode)Present();else Invalidate();
        }
        private void EnsurePresentationMode()
        {
            if(alphaMode==Options.ImageQualityEnabled)return;
            alphaMode=Options.ImageQualityEnabled;
            TransparencyKey=alphaMode?Color.Empty:Color.Fuchsia;
            // SetLayeredWindowAttributes (color key) and UpdateLayeredWindow
            // cannot share the same layered-window state. Recreate at the switch.
            if(IsHandleCreated)RecreateHandle();
            surface.Dispose();
        }
        private void Present() { if(Visible&&ClientSize.Width>0&&ClientSize.Height>0)surface.Draw(Handle,Bounds,DrawOverlay); }
        protected override void OnPaintBackground(PaintEventArgs e) { if(!alphaMode)base.OnPaintBackground(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if(alphaMode)Present();else { base.OnPaint(e);DrawOverlay(e.Graphics); }
        }
        protected override void Dispose(bool disposing) { if(disposing)surface.Dispose();base.Dispose(disposing); }
        private void DrawOverlay(Graphics graphics)
        {
            PaintEventArgs e=new PaintEventArgs(graphics,ClientRectangle);
            if(!Suppressed)Painter.Draw(e.Graphics,ClientSize,Frame,Options,false,!Options.QuickHudVisible&&!Quick.Open);
            if(!Suppressed&&Options.Aim.Enabled&&Painter.Fresh(Frame))
            {
                float radius=Options.Aim.ScreenRadius(ClientSize);
                if(Options.Aim.ShowRadius)using(Pen pen=new Pen(Color.FromArgb(102,160,168),1))e.Graphics.DrawEllipse(pen,ClientSize.Width/2f-radius,ClientSize.Height/2f-radius,radius*2,radius*2);
                if(AimCandidate!=null)using(Pen marker=new Pen(Color.LimeGreen,2))e.Graphics.DrawEllipse(marker,AimCandidate.Point.X-5,AimCandidate.Point.Y-5,10,10);
                if(!Options.QuickHudVisible&&!Quick.Open)using(Font font=new Font("Segoe UI",10))
                {
                    string text=L.Format("AIM {0}% | {1} | {2} | INPUT {3}",Options.Aim.Strength,Options.Aim.PointLabel,AimState,AimMovements);
                    SizeF measured=e.Graphics.MeasureString(text,font);
                    e.Graphics.FillRectangle(Brushes.Black,12,56,Math.Min(ClientSize.Width-24,measured.Width+12),measured.Height+6);
                    e.Graphics.DrawString(text,font,Brushes.LightGray,16,59);
                }
            }
            if(Painter.Fresh(Frame))QuickHud.Draw(e.Graphics,ClientSize,Options,Quick,Suppressed?L.Format("{0}: overlay and assist paused",Options.Shortcuts.Label(1)):AimState,Frame.ReadHertz,AimMovements);
        }
    }
}
