using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class SpectraTheme
    {
        public static readonly Color Background=Color.FromArgb(13,12,20),Surface=Color.FromArgb(24,20,33),Accent=Color.FromArgb(173,68,249),Muted=Color.FromArgb(177,167,196),Border=Color.FromArgb(65,41,87);
        private static Image reference;
        public static Image Reference
        {
            get
            {
                if(reference==null)using(System.IO.Stream stream=typeof(SpectraTheme).Assembly.GetManifestResourceStream("Theme.Reference.png"))if(stream!=null)reference=new Bitmap(stream);
                return reference;
            }
        }
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            GraphicsPath path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Card(Graphics g,RectangleF r,bool bright=false)
        {
            using(GraphicsPath path=Round(r,20))
            using(LinearGradientBrush fill=new LinearGradientBrush(r,bright?Color.FromArgb(48,28,64):Color.FromArgb(25,21,34),Background,90))
            using(Pen border=new Pen(bright?Color.FromArgb(106,58,143):Border,1.4f)) { g.FillPath(fill,path);g.DrawPath(border,path); }
        }
        public static void Text(Graphics g,string text,float x,float y,float width,float height,float size,Color color,bool bold=false)
        {
            using(Font font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(Brush brush=new SolidBrush(color))
            using(StringFormat format=new StringFormat { LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })g.DrawString(text,font,brush,new RectangleF(x,y,width,height),format);
        }
        public static void Toggle(Graphics g,float x,float y,bool enabled,float width=65,float height=34)
        {
            RectangleF r=new RectangleF(x,y,width,height);
            using(GraphicsPath path=Round(r,height/2))using(LinearGradientBrush fill=new LinearGradientBrush(r,enabled?Color.FromArgb(181,61,253):Color.FromArgb(51,44,65),enabled?Color.FromArgb(129,35,222):Color.FromArgb(35,30,46),90))g.FillPath(fill,path);
            using(Brush knob=new SolidBrush(enabled?Color.FromArgb(245,231,255):Color.FromArgb(151,139,168)))g.FillEllipse(knob,enabled?x+width-height+3:x+3,y+3,height-6,height-6);
        }
        public static void Button(Graphics g,RectangleF r,string text,bool enabled=true)
        {
            using(GraphicsPath path=Round(r,16))using(LinearGradientBrush fill=new LinearGradientBrush(r,enabled?Color.FromArgb(174,64,247):Color.FromArgb(57,45,72),enabled?Color.FromArgb(103,29,179):Color.FromArgb(34,28,44),90))using(Pen pen=new Pen(enabled?Color.FromArgb(205,119,255):Border,1.7f)) { g.FillPath(fill,path);g.DrawPath(pen,path); }
            Text(g,text,r.X+25,r.Y,r.Width-45,r.Height,27,enabled?Color.White:Muted,true);
        }
    }
    internal sealed class SpectraSurface : Panel
    {
        public SpectraSurface() { DoubleBuffered=true;BackColor=SpectraTheme.Background; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            base.OnPaint(e);
        }
    }
    internal sealed class SpectraNavButton : Button
    {
        private bool hover;
        public SpectraNavButton() { DoubleBuffered=true; }
        protected override void OnMouseEnter(EventArgs e) { hover=true;base.OnMouseEnter(e);Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover=false;base.OnMouseLeave(e);Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            bool selected=BackColor!=SpectraTheme.Background;
            g.Clear(SpectraTheme.Background);
            if(selected||hover)
            {
                RectangleF r=new RectangleF(1,1,Width-3,Height-3);
                using(GraphicsPath path=SpectraTheme.Round(r,16))using(LinearGradientBrush fill=new LinearGradientBrush(r,Color.FromArgb(30,26,41),selected?Color.FromArgb(130,37,211):SpectraTheme.Surface,0f))using(Pen border=new Pen(selected?Color.FromArgb(142,63,215):SpectraTheme.Border)) { g.FillPath(fill,path);g.DrawPath(border,path); }
            }
            TextRenderer.DrawText(g,Text,Font,new Rectangle(19,0,Width-23,Height),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
            if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(7,7,Width-14,Height-14),ForeColor,SpectraTheme.Background);
        }
    }
    internal sealed class SpectraChoice : Control
    {
        public readonly System.Collections.Generic.List<object> Items=new System.Collections.Generic.List<object>();
        private int selected=-1;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return selected; }
            set { if(value==selected)return;selected=value;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty); }
        }
        public SpectraChoice() { DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;BackColor=SpectraTheme.Surface;ForeColor=Color.White;AccessibleRole=AccessibleRole.ComboBox; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            RectangleF r=new RectangleF(1,1,Width-3,Height-3);using(GraphicsPath path=SpectraTheme.Round(r,8))using(Brush fill=new SolidBrush(BackColor))using(Pen pen=new Pen(Focused?SpectraTheme.Accent:SpectraTheme.Border,1.5f)) { e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path); }
            SpectraTheme.Text(e.Graphics,selected>=0&&selected<Items.Count?Items[selected].ToString():"",13,0,Width-45,Height,16,ForeColor);
            using(Pen arrow=new Pen(SpectraTheme.Muted,2))e.Graphics.DrawLines(arrow,new[]{new Point(Width-25,Height/2-3),new Point(Width-20,Height/2+2),new Point(Width-15,Height/2-3)});
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);Focus();ContextMenuStrip menu=new ContextMenuStrip { BackColor=SpectraTheme.Surface,ForeColor=Color.White,ShowImageMargin=false };
            for(int i=0;i<Items.Count;i++) { int index=i;ToolStripMenuItem item=new ToolStripMenuItem(Items[i].ToString()) { Checked=i==selected,ForeColor=Color.White,BackColor=SpectraTheme.Surface };item.Click+=delegate { SelectedIndex=index; };menu.Items.Add(item); }
            menu.Closed+=delegate { menu.Dispose(); };menu.Show(this,new Point(0,Height));
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down) { SelectedIndex=Math.Max(0,Math.Min(Items.Count-1,selected+(e.KeyCode==Keys.Up?-1:1)));e.Handled=true; }
            else if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space) { OnClick(EventArgs.Empty);e.Handled=true; }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys keyData) { Keys k=keyData&Keys.KeyCode;return k==Keys.Up||k==Keys.Down||base.IsInputKey(keyData); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }
    internal sealed class SpectraSwitch : CheckBox
    {
        public SpectraSwitch() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(4,0,Width-77,Height),ForeColor,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            SpectraTheme.Toggle(e.Graphics,Width-65,2,Checked,58,27);
            if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(0,0,Width-1,Height-1),ForeColor,BackColor);
        }
    }
    internal sealed class SpectraSlider : Control
    {
        private int value;
        public int Minimum { get;set; }
        public int Maximum { get;set; }
        public int SmallChange { get;set; }
        public int LargeChange { get;set; }
        public event EventHandler ValueChanged;
        public int Value { get { return value; }set { int next=Math.Max(Minimum,Math.Min(Maximum,value));if(this.value==next)return;this.value=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty); } }
        public SpectraSlider() { Minimum=0;Maximum=100;SmallChange=1;LargeChange=10;DoubleBuffered=true;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float y=Height/2f,range=Math.Max(1,Maximum-Minimum),x=12+(Width-24)*(value-Minimum)/range;
            using(Pen track=new Pen(Color.FromArgb(46,33,60),6)) { track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,12,y,Width-12,y); }
            using(Pen fill=new Pen(SpectraTheme.Accent,6)) { fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,12,y,Math.Max(13,x),y); }
            using(Brush knob=new SolidBrush(SpectraTheme.Accent))g.FillEllipse(knob,x-9,y-9,18,18);
            if(Focused)using(Pen focus=new Pen(Color.FromArgb(218,175,248)))g.DrawEllipse(focus,x-12,y-12,24,24);
        }
        private void Adjust(int x) { Value=Minimum+(int)Math.Round(Math.Max(0,Math.Min(1,(x-12)/(float)Math.Max(1,Width-24)))*(Maximum-Minimum)); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e);if(e.Button==MouseButtons.Left) { Focus();Capture=true;Adjust(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e);if(Capture)Adjust(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e);Capture=false; }
        protected override bool IsInputKey(Keys keyData) { Keys k=keyData&Keys.KeyCode;return k==Keys.Left||k==Keys.Right||k==Keys.Up||k==Keys.Down||base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Down) { Value-=SmallChange;e.Handled=true; }else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Up) { Value+=SmallChange;e.Handled=true; }else if(e.KeyCode==Keys.Home)Value=Minimum;else if(e.KeyCode==Keys.End)Value=Maximum;else if(e.KeyCode==Keys.PageDown)Value-=LargeChange;else if(e.KeyCode==Keys.PageUp)Value+=LargeChange;base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }
    internal sealed class SpectraDashboard : Control
    {
        private readonly RenderOptions options;
        private readonly Action changed,start;
        private readonly Func<string> emulator,resolution,state;
        private readonly Func<bool> canStart;
        private readonly SpectraChoice target=new SpectraChoice(),activation=new SpectraChoice();
        private int dragging=-1;
        public SpectraDashboard(RenderOptions options,Action changed,Action start,Func<string> emulator,Func<string> resolution,Func<string> state,Func<bool> canStart)
        {
            this.options=options;this.changed=changed;this.start=start;this.emulator=emulator;this.resolution=resolution;this.state=state;this.canStart=canStart;
            DoubleBuffered=true;BackColor=SpectraTheme.Background;Dock=DockStyle.Fill;
            foreach(SpectraChoice c in new[]{target,activation}) { c.Font=new Font("Segoe UI",11);Controls.Add(c); }
            target.Items.AddRange(new object[]{"Head","Chest","Legs"});target.SelectedIndex=Math.Max(0,Array.IndexOf(QuickActions.Points,options.Aim.AimPoint));
            activation.Items.AddRange(new object[]{"Right Mouse (RMB)","Mouse 4","Mouse 5","Alt"});activation.SelectedIndex=Math.Max(0,Array.IndexOf(new[]{2,5,6,18},options.Aim.HoldKey));
            target.SelectedIndexChanged+=delegate { options.Aim.AimPoint=QuickActions.Points[target.SelectedIndex];changed(); };
            activation.SelectedIndexChanged+=delegate { options.Aim.HoldKey=new[]{2,5,6,18}[activation.SelectedIndex];changed(); };
            MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button!=MouseButtons.Left)return;PointF p=Design(e.Location);if(Hit(p,50,178,350,78)&&canStart())start();else if(Hit(p,510,325,74,40))Change(()=>options.EspEnabled=!options.EspEnabled);else if(Hit(p,1160,325,74,40))Change(()=>options.Aim.Enabled=!options.Aim.Enabled);else if(Hit(p,490,401,100,45))Change(()=>options.Boxes=!options.Boxes);else if(Hit(p,490,456,100,45))Change(()=>options.Distance=!options.Distance);else if(Hit(p,490,511,100,45))Change(()=>options.Health=!options.Health);else if(Hit(p,490,566,100,45))Change(()=>options.Lines=!options.Lines);else if(Hit(p,470,629,110,55))ChooseColor();else if(Hit(p,690,448,530,30)) { dragging=0;Capture=true;Adjust(p); }else if(Hit(p,690,519,530,30)) { dragging=1;Capture=true;Adjust(p); } };
            MouseMove+=delegate(object sender,MouseEventArgs e) { if(dragging>=0)Adjust(Design(e.Location)); };
            MouseUp+=delegate { dragging=-1;Capture=false; };
            AccessibleName="SpectraVision dashboard";
        }
        private static bool Hit(PointF p,int x,int y,int w,int h) { return new RectangleF(x,y,w,h).Contains(p); }
        private PointF Design(Point p) { return new PointF(p.X*1280f/Math.Max(1,Width),p.Y*740f/Math.Max(1,Height)); }
        private void Change(Action action) { action();changed();Invalidate(); }
        private void Adjust(PointF p)
        {
            float fraction=Math.Max(0,Math.Min(1,(p.X-690)/530));
            if(dragging==0)options.Aim.Radius=20+(int)Math.Round(fraction*1180);else options.Aim.Strength=(int)Math.Round(fraction*100);
            changed();Invalidate();
        }
        private void ChooseColor()
        {
            using(ColorDialog dialog=new ColorDialog { Color=Color.FromArgb(options.Players.ColorArgb),FullOpen=true })if(dialog.ShowDialog(FindForm())==DialogResult.OK)Change(()=>options.Players.ColorArgb=dialog.Color.ToArgb());
        }
        public void RefreshValues()
        {
            int region=Array.IndexOf(QuickActions.Points,options.Aim.AimPoint);if(region>=0&&target.SelectedIndex!=region)target.SelectedIndex=region;
            int key=Array.IndexOf(new[]{2,5,6,18},options.Aim.HoldKey);if(key>=0&&activation.SelectedIndex!=key)activation.SelectedIndex=key;
            Invalidate();
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);float sx=Width/1280f,sy=Height/740f;
            target.Bounds=new Rectangle((int)(895*sx),(int)(575*sy),(int)(325*sx),32);
            activation.Bounds=new Rectangle((int)(895*sx),(int)(638*sy),(int)(325*sx),32);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;g.ScaleTransform(Width/1280f,Height/740f);
            SpectraTheme.Card(g,new RectangleF(0,0,1278,738));
            if(SpectraTheme.Reference!=null)
            {
                using(GraphicsPath clip=SpectraTheme.Round(new RectangleF(845,2,430,337),20)) { GraphicsState save=g.Save();g.SetClip(clip);g.DrawImage(SpectraTheme.Reference,new RectangleF(845,2,430,337),new RectangleF(1188,94,430,337),GraphicsUnit.Pixel);g.Restore(save); }
                g.DrawImage(SpectraTheme.Reference,new RectangleF(30,34,120,110),new RectangleF(351,124,135,114),GraphicsUnit.Pixel);
            }
            SpectraTheme.Text(g,"SpectraVision",164,32,650,76,52,Color.FromArgb(224,188,255),true);
            SpectraTheme.Text(g,"PUBG Mobile Assistant ("+emulator()+")",167,103,650,36,20,SpectraTheme.Muted);
            SpectraTheme.Card(g,new RectangleF(610,52,180,52));SpectraTheme.Text(g,state(),632,55,146,44,21,Color.White);
            SpectraTheme.Card(g,new RectangleF(20,152,410,128));SpectraTheme.Button(g,new RectangleF(50,178,350,78),"▶  START",canStart());
            SpectraTheme.Card(g,new RectangleF(445,152,400,128));
            string[] labels={"Target:","Game:","Resolution:"};string[] values={emulator(),"PUBG Mobile",resolution()};
            for(int i=0;i<3;i++) { SpectraTheme.Text(g,labels[i],473,163+i*35,160,32,21,Color.White);SpectraTheme.Text(g,values[i],638,163+i*35,199,32,21,Color.White); }
            SpectraTheme.Card(g,new RectangleF(30,309,590,400),true);SpectraTheme.Card(g,new RectangleF(646,309,603,400),true);
            SpectraTheme.Text(g,"⊕  ESP",59,324,410,48,31,Color.White,true);SpectraTheme.Toggle(g,515,330,options.EspEnabled);
            SpectraTheme.Text(g,"➶  Aim Assist",677,324,460,48,31,Color.White,true);SpectraTheme.Toggle(g,1168,330,options.Aim.Enabled);
            string[] espLabels={"Box","Distance","Health","Snapline"};bool[] toggles={options.Boxes,options.Distance,options.Health,options.Lines};
            for(int i=0;i<4;i++)
            {
                float y=388+i*55;
                using(Pen line=new Pen(Color.FromArgb(40,33,52)))g.DrawLine(line,55,y,595,y);
                SpectraTheme.Text(g,"◆",72,y+5,37,45,25,SpectraTheme.Accent);SpectraTheme.Text(g,espLabels[i],117,y+5,340,45,22,Color.White);SpectraTheme.Toggle(g,523,y+13,toggles[i],58,30);
            }
            SpectraTheme.Card(g,new RectangleF(55,628,540,61));SpectraTheme.Text(g,"Color:",71,636,250,43,22,Color.White);
            using(Brush color=new SolidBrush(Color.FromArgb(options.Players.ColorArgb)))using(Pen outline=new Pen(Color.FromArgb(223,196,246),2)) { g.FillRectangle(color,477,641,39,36);g.DrawRectangle(outline,477,641,39,36); }
            SpectraTheme.Text(g,"FOV radius",687,396,390,36,21,Color.White);SpectraTheme.Text(g,options.Aim.Radius+" px",1120,396,103,36,21,Color.White);Track(g,690,462,(options.Aim.Radius-20)/1180f);
            SpectraTheme.Text(g,"Strength",687,475,390,36,21,SpectraTheme.Muted);SpectraTheme.Text(g,options.Aim.Strength+"%",1152,475,80,36,21,Color.White);Track(g,690,533,options.Aim.Strength/100f);
            SpectraTheme.Text(g,"Target:",687,568,195,42,21,Color.White);SpectraTheme.Text(g,"Hold Key:",687,631,195,42,21,Color.White);
            SpectraTheme.Text(g,"Mode: "+options.Aim.Activation,688,677,530,23,15,SpectraTheme.Muted);
        }
        private static void Track(Graphics g,float x,float y,float fraction)
        {
            using(Pen track=new Pen(Color.FromArgb(48,36,66),8)) { track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,x,y,x+530,y); }
            using(Pen fill=new Pen(SpectraTheme.Accent,8)) { fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,x,y,x+Math.Max(1,530*fraction),y); }
            using(Brush glow=new SolidBrush(Color.FromArgb(45,170,60,250)))g.FillEllipse(glow,x+530*fraction-18,y-18,36,36);
            using(Brush knob=new SolidBrush(SpectraTheme.Accent))g.FillEllipse(knob,x+530*fraction-11,y-11,22,22);
        }
    }
    public sealed partial class MainForm
    {
        private Panel shellContent;
        private Control connectionPage;
        private SpectraDashboard dashboard;
        private OptionsForm embeddedOptions;
        private DateTime nextShellRefresh;
        private readonly System.Collections.Generic.List<Button> shellNavigation=new System.Collections.Generic.List<Button>();
        private void BuildSpectraShell(Control oldLayout)
        {
            SuspendLayout();Controls.Remove(oldLayout);connectionPage=oldLayout;
            Text="SpectraVision | ESP Workbench";FormBorderStyle=FormBorderStyle.None;MinimumSize=new Size(1120,720);Size=new Size(1440,820);Padding=new Padding(12);DoubleBuffered=true;MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;
            TableLayoutPanel shell=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,BackColor=Background,Margin=Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,48));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,62));Controls.Add(shell);
            SpectraSurface sidebar=new SpectraSurface { Dock=DockStyle.Fill,Padding=new Padding(15,12,15,0) };shell.Controls.Add(sidebar,0,0);shell.SetRowSpan(sidebar,3);
            sidebar.Paint+=delegate(object sender,PaintEventArgs e) { if(SpectraTheme.Reference!=null)e.Graphics.DrawImage(SpectraTheme.Reference,new Rectangle(75,10,80,76),new Rectangle(128,36,84,78),GraphicsUnit.Pixel); };
            string[] titles={"⌂   Home","⊕   ESP","➶   Aim Assist","☷   Visuals","⚙   Settings","⌨   Shortcuts","ⓘ   About"};
            for(int i=0;i<titles.Length;i++)
            {
                int page=i;Button button=new SpectraNavButton { Text=titles[i],Bounds=new Rectangle(14,112+i*76,200,62),TextAlign=ContentAlignment.MiddleLeft,Padding=new Padding(17,0,0,0),FlatStyle=FlatStyle.Flat,ForeColor=SpectraTheme.Muted,BackColor=Background,Font=new Font("Segoe UI",15),Cursor=Cursors.Hand };
                button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Surface;button.Click+=delegate { NavigateShell(page); };sidebar.Controls.Add(button);shellNavigation.Add(button);
            }
            SpectraSurface top=new SpectraSurface { Dock=DockStyle.Fill };shell.Controls.Add(top,1,0);
            FlowLayoutPanel windowButtons=new FlowLayoutPanel { Dock=DockStyle.Right,Width=150,WrapContents=false };
            string[] windowLabels={"—","□","×"};for(int i=0;i<3;i++) { int index=i;Button b=new Button { Text=windowLabels[i],Width=46,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Background,ForeColor=Color.Gainsboro,Font=new Font("Segoe UI",16),TabStop=false };b.FlatAppearance.BorderSize=0;b.Click+=delegate { if(index==0)WindowState=FormWindowState.Minimized;else if(index==1)WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;else Close(); };windowButtons.Controls.Add(b); }top.Controls.Add(windowButtons);
            top.MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left) { ShellReleaseCapture();ShellSendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero); } };top.DoubleClick+=delegate { WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized; };
            shellContent=new Panel { Dock=DockStyle.Fill,Margin=new Padding(4,0,4,10),BackColor=Background };shell.Controls.Add(shellContent,1,1);
            SpectraSurface footer=new SpectraSurface { Dock=DockStyle.Fill,Margin=new Padding(4,0,4,4) };shell.Controls.Add(footer,1,2);
            footer.Paint+=delegate(object sender,PaintEventArgs e) {
                SpectraTheme.Card(e.Graphics,new RectangleF(0,0,Math.Max(2,footer.Width-2),Math.Max(2,footer.Height-2)));
                string mode=liveDisplay?"LIVE":demoMode?"DEMO":busy?"Working":"Ready";
                SpectraTheme.Text(e.Graphics,"⚙  Status: "+mode,18,5,220,40,17,liveDisplay?Color.LimeGreen:SpectraTheme.Muted);
                SpectraTheme.Text(e.Graphics,(latest==null?"Reader: —":"Reader: "+latest.ReadHertz.ToString("0")+" Hz"),240,5,200,40,17,SpectraTheme.Muted);
                SpectraTheme.Text(e.Graphics,status.Text,440,5,Math.Max(50,footer.Width-460),40,15,SpectraTheme.Muted);
            };
            dashboard=new SpectraDashboard(options,ChangedQuickOptions,async delegate { if(live.Enabled)await StartLive(); },()=>Emulators.Name(activeEmulator),()=>{ Instance selected=instances.SelectedItem as Instance;Rectangle r=selected==null?Rectangle.Empty:Native.ClientBounds(selected.Window);return r.Width>0?r.Width+" × "+r.Height:"—"; },()=>liveDisplay?"● Live":demoMode?"◉ Demo":busy?"◌ Working":"◉ Idle",()=>live.Enabled);
            timer.Tick+=delegate {
                if(DateTime.UtcNow<nextShellRefresh)return;nextShellRefresh=DateTime.UtcNow.AddMilliseconds(250);
                if(dashboard.Visible)dashboard.Invalidate();footer.Invalidate();
            };
            NavigateShell(0);ResumeLayout(true);
        }
        private void NavigateShell(int page)
        {
            if(page==5) { OpenMenu(8);return; }
            // Reparent persistent pages before disposing a temporary embedded settings form.
            shellContent.Controls.Clear();
            if(embeddedOptions!=null) { embeddedOptions.Dispose();embeddedOptions=null;SaveOptions(); }
            for(int i=0;i<shellNavigation.Count;i++) { shellNavigation[i].BackColor=i==page?Color.FromArgb(73,28,113):Background;shellNavigation[i].ForeColor=i==page?Color.White:SpectraTheme.Muted; }
            if(page==0) { dashboard.RefreshValues();shellContent.Controls.Add(dashboard); }
            else if(page==4) { shellContent.Controls.Add(connectionPage); }
            else if(page==6)
            {
                Label about=new Label { Dock=DockStyle.Fill,Text="SpectraVision\nESP Workbench · LDPlayer / GameLoop\n\nPUBG Mobile Assistant\n\nInsert  ·  Collapse / expand overlay\nCustomize your keys in Shortcuts",ForeColor=SpectraTheme.Muted,Font=new Font("Segoe UI",21),TextAlign=ContentAlignment.MiddleCenter };shellContent.Controls.Add(about);
            }
            else
            {
                embeddedOptions=new OptionsForm(options,SaveOptions,CaptureShortcuts) { TopLevel=false,FormBorderStyle=FormBorderStyle.None,Dock=DockStyle.Fill };
                embeddedOptions.FormClosed+=delegate { if(!closing&&!IsDisposed)BeginInvoke(new Action(()=>NavigateShell(0))); };
                embeddedOptions.PrepareEmbedded(page==2?1:page==3?7:0);shellContent.Controls.Add(embeddedOptions);embeddedOptions.Show();
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(GraphicsPath border=SpectraTheme.Round(new RectangleF(2,2,Width-5,Height-5),24))using(Pen pen=new Pen(SpectraTheme.Border,2))e.Graphics.DrawPath(pen,border);
        }
        public void RenderHome(string path)
        {
            // Render without connecting to a running emulator or starting a session.
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
            Hide();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="ReleaseCapture")]private static extern bool ShellReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr ShellSendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
    }
}
