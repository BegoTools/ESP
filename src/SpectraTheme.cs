using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class SpectraTheme
    {
        public static readonly Color Background=Color.FromArgb(10,15,22),Surface=Color.FromArgb(20,29,39),
            Accent=Color.FromArgb(71,231,255),Magenta=Color.FromArgb(247,92,229),Muted=Color.FromArgb(161,178,194),
            Border=Color.FromArgb(54,76,94),Selected=Color.FromArgb(19,59,72),Green=Color.FromArgb(91,245,157);
        public static GraphicsPath Round(RectangleF r,float radius)
        {
            GraphicsPath path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));
            path.AddArc(r.X,r.Y,d,d,180,90);path.AddArc(r.Right-d,r.Y,d,d,270,90);path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.X,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        public static void Card(Graphics g,RectangleF r,bool bright=false,int opacity=255,float radius=14,Color? solidFill=null)
        {
            using(GraphicsPath path=Round(r,radius))
            using(Brush fill=solidFill.HasValue?(Brush)new SolidBrush(solidFill.Value):
                new LinearGradientBrush(r,Color.FromArgb(opacity,30,42,54),Color.FromArgb(opacity,13,20,30),90))
            {
                g.FillPath(fill,path);
                if(bright) NeonOutline(g,path,r);
                else using(Pen border=new Pen(Border))g.DrawPath(border,path);
            }
        }
        internal static void PaintBackground(PaintEventArgs e,Color color)
        {
            using(var fill=new SolidBrush(color))e.Graphics.FillRectangle(fill,e.ClipRectangle);
        }
        internal static void DrawCaption(Graphics g,string text,Font font,Rectangle bounds,Color color,TextFormatFlags flags)
        {
            TextRenderer.DrawText(g,text,font,bounds,color,flags|TextFormatFlags.NoPrefix|
                TextFormatFlags.PreserveGraphicsClipping|TextFormatFlags.PreserveGraphicsTranslateTransform);
        }
        public static void NeonOutline(Graphics g,GraphicsPath path,RectangleF r)
        {
            using(LinearGradientBrush gradient=new LinearGradientBrush(r,Accent,Magenta,0f))
            using(Pen edge=new Pen(gradient,1.4f))
            {
                gradient.LinearColors=new[]{Color.FromArgb(38,Accent),Color.FromArgb(38,Magenta)};
                using(Pen halo=new Pen(gradient,5))g.DrawPath(halo,path);
                g.DrawPath(edge,path);
            }
        }
        public static void Wordmark(Graphics g,RectangleF bounds,bool stacked)
        {
            RectangleF chip=stacked?new RectangleF(37,1,66,18):new RectangleF(0,10,55,17);
            GraphicsState state=g.Save();
            try
            {
                g.TranslateTransform(bounds.X,bounds.Y);
                g.ScaleTransform(bounds.Width/(stacked?140:260),bounds.Height/(stacked?98:37));
                PointF[] outline={new PointF(chip.X,chip.Y+6),new PointF(chip.X+5,chip.Y),new PointF(chip.Right-1,chip.Y),
                    new PointF(chip.Right-5,chip.Bottom-1),new PointF(chip.X+1,chip.Bottom-1)};
                using(GraphicsPath path=new GraphicsPath())
                {
                    path.AddPolygon(outline);
                    NeonOutline(g,path,chip);
                }
                using(Pen pin=new Pen(Color.FromArgb(230,186,96),1))
                using(Pen slot=new Pen(Accent,2))
                {
                    for(int i=0;i<12;i++)g.DrawLine(pin,chip.X+7+i*3,chip.Bottom,chip.X+7+i*3,chip.Bottom+2);
                    for(int i=0;i<3;i++)g.DrawLine(slot,chip.X+13+i*6,chip.Y+12,chip.X+16+i*6,chip.Y+6);
                }
                LogoWord(g,"RAM",stacked?new RectangleF(1,24,138,34):new RectangleF(65,3,91,31),Accent,Color.White);
                LogoWord(g,"HAX",stacked?new RectangleF(10,61,120,33):new RectangleF(163,3,96,31),Magenta,Color.FromArgb(255,192,247));
            }
            finally { g.Restore(state); }
        }
        private static void LogoWord(Graphics g,string word,RectangleF r,Color tint,Color shine)
        {
            using(GraphicsPath path=new GraphicsPath())
            using(Matrix transform=new Matrix())
            using(StringFormat format=StringFormat.GenericTypographic)
            {
                path.AddString(word,FontFamily.GenericSansSerif,(int)(FontStyle.Bold|FontStyle.Italic),40,PointF.Empty,format);
                RectangleF source=path.GetBounds();
                transform.Translate(r.X,r.Y);
                transform.Scale(r.Width/source.Width,r.Height/source.Height);
                transform.Translate(-source.X,-source.Y);
                path.Transform(transform);
                using(LinearGradientBrush metal=new LinearGradientBrush(r,shine,tint,90))
                using(Pen glow=new Pen(Color.FromArgb(55,tint),4))
                using(Pen edge=new Pen(tint,.85f))
                {
                    metal.InterpolationColors=new ColorBlend {
                        Colors=new[]{tint,shine,Color.FromArgb(48,60,85),shine,tint},
                        Positions=new[]{0f,.32f,.48f,.63f,1f}
                    };
                    g.DrawPath(glow,path);g.FillPath(metal,path);g.DrawPath(edge,path);
                }
            }
        }
        public static void NearbyGraphic(Graphics g,RectangleF r)
        {
            float x=r.X+r.Width/2,y=r.Y+r.Height/2,radius=Math.Min(r.Width,r.Height)*.45f;
            using(Pen grid=new Pen(Color.FromArgb(60,Accent)))
            using(Brush sweep=new SolidBrush(Color.FromArgb(43,Accent)))
            using(Brush point=new SolidBrush(Accent))
            {
                for(int i=1;i<=4;i++)
                {
                    float ring=radius*i/4;
                    g.DrawEllipse(grid,x-ring,y-ring,ring*2,ring*2);
                }
                g.DrawLine(grid,x-radius,y,x+radius,y);
                g.DrawLine(grid,x,y-radius,x,y+radius);
                g.FillPie(sweep,x-radius,y-radius,radius*2,radius*2,270,55);
                g.FillEllipse(point,x-2,y-2,4,4);
            }
        }
        public static void Text(Graphics g,string text,float x,float y,float width,float height,float size,Color color,bool bold=false)
        {
            using(Font font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
            using(Brush brush=new SolidBrush(color))
            using(StringFormat format=new StringFormat { LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap }){ if(L.Rtl) { format.FormatFlags|=StringFormatFlags.DirectionRightToLeft;format.Alignment=StringAlignment.Far; }g.DrawString(text,font,brush,new RectangleF(x,y,width,height),format); }
        }
        public static void Toggle(Graphics g,float x,float y,bool enabled,float width=65,float height=34)
        {
            RectangleF r=new RectangleF(x,y,width,height);
            using(GraphicsPath path=Round(r,height/2))using(Brush fill=new SolidBrush(enabled?Selected:Color.FromArgb(47,58,71)))g.FillPath(fill,path);
            using(Brush knob=new SolidBrush(enabled?Accent:Muted))g.FillEllipse(knob,enabled?x+width-height+3:x+3,y+3,height-6,height-6);
        }
        public static void Button(Graphics g,RectangleF r,string text,bool enabled=true)
        {
            using(GraphicsPath path=Round(r,10))using(LinearGradientBrush fill=new LinearGradientBrush(r,enabled?Color.FromArgb(166,37,164):Surface,enabled?Color.FromArgb(15,111,146):Background,0f))
            using(Pen pen=new Pen(enabled?Accent:Border,1.3f)) { g.FillPath(fill,path);g.DrawPath(pen,path); }
            using(Font font=new Font("Segoe UI",Math.Min(24,r.Height*.42f),FontStyle.Bold,GraphicsUnit.Pixel))
            using(Brush brush=new SolidBrush(enabled?Color.White:Muted))
            using(StringFormat format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,
                FormatFlags=StringFormatFlags.NoWrap|(L.Rtl?StringFormatFlags.DirectionRightToLeft:0) })g.DrawString(text,font,brush,r,format);
        }
        public static void DragWindow(IntPtr handle) { ReleaseCapture();SendMessage(handle,0xA1,new IntPtr(2),IntPtr.Zero); }
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SendMessageW")]private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wparam,IntPtr lparam);
    }
    internal sealed class SpectraPrimaryButton : Button
    {
        public SpectraPrimaryButton() { DoubleBuffered=true;Cursor=Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e)
        {
            SpectraTheme.PaintBackground(e,BackColor);
            if(Width<45||Height<7)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            SpectraTheme.Button(e.Graphics,new RectangleF(2,2,Width-4,Height-4),Text,Enabled);
            if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(8,8,Width-16,Height-16),Color.White,BackColor);
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
        internal string Glyph { get; set; }
        public SpectraNavButton() { DoubleBuffered=true; }
        protected override void OnMouseEnter(EventArgs e) { hover=true;base.OnMouseEnter(e);Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { hover=false;base.OnMouseLeave(e);Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            bool selected=BackColor!=SpectraTheme.Background;
            SpectraTheme.PaintBackground(e,SpectraTheme.Background);
            if(selected||hover)
            {
                RectangleF r=new RectangleF(1,1,Width-3,Height-3);
                using(GraphicsPath path=SpectraTheme.Round(r,9))
                using(LinearGradientBrush fill=new LinearGradientBrush(r,selected?SpectraTheme.Selected:SpectraTheme.Surface,SpectraTheme.Background,0f))
                using(Pen border=new Pen(selected?SpectraTheme.Accent:SpectraTheme.Border))
                { g.FillPath(fill,path);g.DrawPath(border,path); }
            }
            bool rtl=RightToLeft==RightToLeft.Yes;
            Rectangle iconBounds=new Rectangle(rtl?Width-42:17,0,23,Height);
            Rectangle labelBounds=new Rectangle(rtl?12:45,0,Width-57,Height);
            SpectraTheme.DrawCaption(g,Glyph,Font,iconBounds,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
            TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|
                (rtl?TextFormatFlags.RightToLeft|TextFormatFlags.Right:TextFormatFlags.Left);
            int naturalWidth=TextRenderer.MeasureText(g,Text,Font,Size.Empty,TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine).Width;
            if(naturalWidth>labelBounds.Width)
            {
                float size=Math.Max(10,Font.Size*labelBounds.Width/naturalWidth);
                using(Font compact=new Font(Font.FontFamily,size,Font.Style))
                    SpectraTheme.DrawCaption(g,Text,compact,labelBounds,ForeColor,flags);
            }
            else SpectraTheme.DrawCaption(g,Text,Font,labelBounds,ForeColor,flags);
            if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(7,7,Width-14,Height-14),ForeColor,SpectraTheme.Background);
        }
    }
    internal sealed class SpectraChoice : Control
    {
        public readonly System.Collections.Generic.List<object> Items=new System.Collections.Generic.List<object>();
        private int selected=-1;
        private ContextMenuStrip menu;
        public event EventHandler SelectedIndexChanged;
        public int SelectedIndex
        {
            get { return selected; }
            set { if(value==selected)return;selected=value;Invalidate();if(SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty); }
        }
        public object SelectedItem
        {
            get { return selected>=0&&selected<Items.Count?Items[selected]:null; }
            set { SelectedIndex=Items.IndexOf(value); }
        }
        internal string SelectionText { get { return SelectedItem==null?"":SelectedItem.ToString(); } }
        internal bool WrapSelection { get; set; }
        private TextFormatFlags SelectionFlags
        {
            get
            {
                TextFormatFlags align=RightToLeft==RightToLeft.Yes?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left;
                return align|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|
                    (WrapSelection?TextFormatFlags.WordBreak:TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            }
        }
        internal int PreferredHeight(int width)
        {
            if(!WrapSelection)return 0;
            int height=0;
            var area=new Size(Math.Max(1,width-45),Int32.MaxValue);
            foreach(object item in Items)
                height=Math.Max(height,TextRenderer.MeasureText(item.ToString(),Font,area,SelectionFlags).Height);
            return height+8;
        }
        internal void ReplaceItems(System.Collections.Generic.IEnumerable<object> items)
        {
            if(menu!=null)menu.Close();
            Items.Clear();
            Items.AddRange(items);
            selected=-1;
            Invalidate();
        }
        public SpectraChoice() { DoubleBuffered=true;SetStyle(ControlStyles.Opaque,true);TabStop=true;Cursor=Cursors.Hand;BackColor=SpectraTheme.Surface;ForeColor=Color.White;AccessibleRole=AccessibleRole.ComboBox; }
        protected override void OnPaint(PaintEventArgs e)
        {
            SpectraTheme.PaintBackground(e,BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            RectangleF r=new RectangleF(1,1,Width-3,Height-3);using(GraphicsPath path=SpectraTheme.Round(r,8))using(Brush fill=new SolidBrush(BackColor))using(Pen pen=new Pen(Focused?SpectraTheme.Accent:SpectraTheme.Border,1.5f)) { e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path); }
            Color text=Enabled&&SelectedItem!=null?ForeColor:SpectraTheme.Muted;
            SpectraTheme.DrawCaption(e.Graphics,SelectedItem==null?"—":SelectionText,Font,
                new Rectangle(13,0,Width-45,Height),text,SelectionFlags);
            using(Pen arrow=new Pen(SpectraTheme.Muted,2))e.Graphics.DrawLines(arrow,new[]{new Point(Width-25,Height/2-3),new Point(Width-20,Height/2+2),new Point(Width-15,Height/2-3)});
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Items.Count == 0 || IsDisposed || !Enabled) return;
            Focus();
            if (menu == null)
            {
                menu = new ContextMenuStrip
                {
                    BackColor = SpectraTheme.Surface,
                    ForeColor = Color.White,
                    ShowImageMargin = false,
                    Renderer=new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges=false }
                };
            }
            if (menu.Visible) return;
            while (menu.Items.Count > 0) menu.Items[0].Dispose();
            menu.RightToLeft = RightToLeft;
            for (int i = 0; i < Items.Count; i++)
            {
                int index = i;
                ToolStripMenuItem item = new ToolStripMenuItem(Items[i].ToString())
                {
                    Checked = i == selected,
                    ForeColor = Color.White,
                    BackColor = SpectraTheme.Surface,
                    Font=Font,
                    Padding=new Padding(8,4,8,4)
                };
                item.ToolTipText=item.Text;
                item.Click += delegate { SelectedIndex = index; };
                menu.Items.Add(item);
            }
            menu.Show(this, new Point(0, Height));
        }
        private sealed class MenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return SpectraTheme.Surface; } }
            public override Color MenuBorder { get { return SpectraTheme.Border; } }
            public override Color MenuItemBorder { get { return SpectraTheme.Accent; } }
            public override Color MenuItemSelected { get { return SpectraTheme.Selected; } }
            public override Color MenuItemSelectedGradientBegin { get { return SpectraTheme.Selected; } }
            public override Color MenuItemSelectedGradientEnd { get { return SpectraTheme.Selected; } }
            public override Color CheckBackground { get { return SpectraTheme.Selected; } }
            public override Color CheckSelectedBackground { get { return SpectraTheme.Selected; } }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && menu != null)
            {
                menu.Dispose();
                menu = null;
            }
            base.Dispose(disposing);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if(Items.Count>0&&(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)) { SelectedIndex=Math.Max(0,Math.Min(Items.Count-1,selected+(e.KeyCode==Keys.Up?-1:1)));e.Handled=true; }
            else if(e.KeyCode==Keys.Enter||e.KeyCode==Keys.Space) { OnClick(EventArgs.Empty);e.Handled=true; }
            base.OnKeyDown(e);
        }
        protected override bool IsInputKey(Keys keyData) { Keys k=keyData&Keys.KeyCode;return k==Keys.Up||k==Keys.Down||base.IsInputKey(keyData); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }
    internal sealed class SpectraSwitch : CheckBox
    {
        public SpectraSwitch()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Opaque,true);
            Cursor=Cursors.Hand;
        }
        private Rectangle CaptionArea(int width,int height)
        {
            return new Rectangle(RightToLeft==RightToLeft.Yes?64:4,0,Math.Max(1,width-68),height);
        }
        private TextFormatFlags CaptionFlags
        {
            get
            {
                return TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|
                    (RightToLeft==RightToLeft.Yes?TextFormatFlags.Right|TextFormatFlags.RightToLeft:TextFormatFlags.Left);
            }
        }
        internal int PreferredHeight(int width)
        {
            return Math.Max(32,TextRenderer.MeasureText(Text,Font,CaptionArea(width,Int32.MaxValue).Size,CaptionFlags).Height+8);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color surface=Parent==null?SpectraTheme.Background:BackColor;
            SpectraTheme.PaintBackground(e,surface);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            SpectraTheme.DrawCaption(e.Graphics,Text,Font,CaptionArea(Width,Height),Enabled?ForeColor:SpectraTheme.Muted,CaptionFlags);
            SpectraTheme.Toggle(e.Graphics,RightToLeft==RightToLeft.Yes?6:Width-54,(Height-22)/2,Checked,48,22);
            if(Focused)
                using(var pen=new Pen(SpectraTheme.Muted) { DashStyle=DashStyle.Dot })
                    e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
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
        public SpectraSlider() { Minimum=0;Maximum=100;SmallChange=1;LargeChange=10;DoubleBuffered=true;SetStyle(ControlStyles.Opaque,true);TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.Slider; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;SpectraTheme.PaintBackground(e,Parent==null?SpectraTheme.Background:BackColor);g.SmoothingMode=SmoothingMode.AntiAlias;float y=Height/2f,range=Math.Max(1,Maximum-Minimum),x=12+(Width-24)*(value-Minimum)/range;
            using(Pen track=new Pen(SpectraTheme.Border,6)) { track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,12,y,Width-12,y); }
            using(Pen fill=new Pen(SpectraTheme.Accent,6)) { fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,12,y,Math.Max(13,x),y); }
            using(Brush knob=new SolidBrush(SpectraTheme.Accent))g.FillEllipse(knob,x-9,y-9,18,18);
            if(Focused)using(Pen focus=new Pen(SpectraTheme.Accent))g.DrawEllipse(focus,x-12,y-12,24,24);
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
        private static readonly Rectangle StartButton=new Rectangle(1039,32,193,56);
        private static readonly Rectangle ColorPicker=new Rectangle(483,344,113,40);
        private static readonly Rectangle DistanceTrack=new Rectangle(45,367,394,24);
        private static readonly Rectangle RadiusTrack=new Rectangle(679,540,548,22);
        private static readonly Rectangle StrengthTrack=new Rectangle(679,594,548,22);
        private readonly RenderOptions options;
        private readonly Action changed,start;
        private readonly Func<string> emulator,resolution,state,licenseState;
        private readonly Func<bool> canStart;
        private readonly Func<Frame> frame;
        private readonly SpectraChoice target=new SpectraChoice(),activation=new SpectraChoice();
        private readonly ToggleTile[] playerTiles,lootTiles;
        private readonly ToggleTile lootMaster,aimMaster,panelVisibility;
        private Rectangle dragging;
        private static readonly int[] HoldKeys={2,5,6,18};

        private sealed class ToggleTile
        {
            public readonly Rectangle Bounds;
            private readonly string label;
            private readonly Func<bool> value;
            private readonly Action toggle;
            public ToggleTile(Rectangle bounds,string label,Func<bool> value,Action toggle)
            { Bounds=bounds;this.label=label;this.value=value;this.toggle=toggle; }

            public void Draw(Graphics g)
            {
                bool enabled=value();
                SpectraTheme.Text(g,L.Text(label??(enabled?"On":"Off")),Bounds.X+6,Bounds.Y,Bounds.Width-60,Bounds.Height,19,Color.White);
                SpectraTheme.Toggle(g,Bounds.Right-46,Bounds.Y+(Bounds.Height-23)/2,enabled,43,23);
            }
            public bool Click(PointF p)
            {
                if(!((RectangleF)Bounds).Contains(p))return false;
                toggle();
                return true;
            }
        }

        public SpectraDashboard(RenderOptions options,Action changed,Action start,Func<string> emulator,
            Func<string> resolution,Func<string> state,Func<bool> canStart,Func<Frame> frame,Func<string> licenseState,
            Func<bool> panelShown,Action togglePanel)
        {
            this.options=options;this.changed=changed;this.start=start;this.emulator=emulator;this.resolution=resolution;
            this.state=state;this.canStart=canStart;this.frame=frame;this.licenseState=licenseState;
            DoubleBuffered=true;BackColor=SpectraTheme.Background;Dock=DockStyle.Fill;
            playerTiles=new[]{
                Tile(44,190,251,"Boxes",()=>options.Boxes,()=>options.Boxes=!options.Boxes),
                Tile(335,190,259,"الصحة",()=>options.Health,()=>options.Health=!options.Health),
                Tile(44,237,251,"Names",()=>options.Names,()=>options.Names=!options.Names),
                Tile(335,237,259,"المسافة",()=>options.Distance,()=>options.Distance=!options.Distance),
                Tile(44,284,251,"Snapline",()=>options.Lines,()=>options.Lines=!options.Lines),
                Tile(335,284,259,"ESP",()=>options.EspEnabled,()=>options.EspEnabled=!options.EspEnabled)
            };
            lootMaster=Tile(1007,139,225,null,()=>options.Loot.Enabled,()=>options.Loot.Enabled=!options.Loot.Enabled);
            lootTiles=new[]{
                Tile(675,190,258,"الأسلحة",()=>options.LootWeapons,()=>options.LootWeapons=!options.LootWeapons),
                Tile(968,190,259,"الذخيرة",()=>options.LootAmmo,()=>options.LootAmmo=!options.LootAmmo),
                Tile(675,237,258,"Armor",()=>options.LootArmor,()=>options.LootArmor=!options.LootArmor),
                Tile(968,237,259,"Medical",()=>options.LootHealing,()=>options.LootHealing=!options.LootHealing),
                Tile(675,284,258,"المناظير",()=>options.LootOptics,()=>options.LootOptics=!options.LootOptics),
                Tile(968,284,259,"Equipment",()=>options.LootEquipment,()=>options.LootEquipment=!options.LootEquipment),
                Tile(675,331,258,"أنواع أخرى",()=>options.LootOther,()=>options.LootOther=!options.LootOther)
            };
            aimMaster=Tile(1007,437,225,null,()=>options.Aim.Enabled,()=>options.Aim.Enabled=!options.Aim.Enabled);
            panelVisibility=Tile(784,42,233,"In-game panel",panelShown,togglePanel);
            foreach(SpectraChoice c in new[]{target,activation}) { c.Font=new Font("Segoe UI",11);Controls.Add(c); }
            RefreshLanguage();
            RefreshValues();
            target.SelectedIndexChanged+=delegate { if(target.SelectedIndex>=0)Change(()=>options.Aim.AimPoint=QuickActions.Points[target.SelectedIndex]); };
            activation.SelectedIndexChanged+=delegate { if(activation.SelectedIndex>=0)Change(()=>options.Aim.HoldKey=HoldKeys[activation.SelectedIndex]); };
        }

        private static ToggleTile Tile(int x,int y,int width,string label,Func<bool> value,Action toggle)
        {
            return new ToggleTile(new Rectangle(x,y,width,38),label,value,toggle);
        }

        private PointF Design(Point p)
        {
            return new PointF(p.X*1280f/Math.Max(1,Width),p.Y*740f/Math.Max(1,Height));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button!=MouseButtons.Left)return;
            PointF p=Design(e.Location);
            if(((RectangleF)StartButton).Contains(p)) { if(canStart())start();return; }
            if(((RectangleF)ColorPicker).Contains(p)) { ChooseColor();return; }
            if(panelVisibility.Click(p)) { Invalidate();return; }
            if(lootMaster.Click(p)||aimMaster.Click(p)) { Change(null);return; }
            foreach(ToggleTile tile in playerTiles)if(tile.Click(p)) { Change(null);return; }
            foreach(ToggleTile tile in lootTiles)if(tile.Click(p)) { Change(null);return; }
            foreach(Rectangle track in new[]{DistanceTrack,RadiusTrack,StrengthTrack})
            {
                if(!((RectangleF)track).Contains(p))continue;
                dragging=track;
                Capture=true;
                Adjust(p);
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if(Capture&&!dragging.IsEmpty)Adjust(Design(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging=Rectangle.Empty;
            Capture=false;
        }

        private void Change(Action action)
        {
            if(action!=null)action();
            changed();
            Invalidate();
        }

        private void Adjust(PointF p)
        {
            float fraction=Math.Max(0,Math.Min(1,(p.X-dragging.X)/dragging.Width));
            if(dragging==DistanceTrack)options.MaxDistance=10+(float)Math.Round(fraction*4990);
            else QuickActions.SetAdjustment(options,dragging==RadiusTrack?1:0,fraction);
            Change(null);
        }

        private void ChooseColor()
        {
            using(ColorDialog dialog=new ColorDialog { Color=Color.FromArgb(options.Players.ColorArgb),FullOpen=true })
                if(dialog.ShowDialog(FindForm())==DialogResult.OK)Change(()=>options.Players.ColorArgb=dialog.Color.ToArgb());
        }

        public void RefreshLanguage()
        {
            target.Items.Clear();
            target.Items.AddRange(new object[]{L.Text("Head"),L.Text("Chest"),L.Text("Legs")});
            activation.Items.Clear();
            activation.Items.AddRange(new object[]{L.Text("Right Mouse (RMB)"),L.Text("Mouse 4"),L.Text("Mouse 5"),"Alt"});
            target.RightToLeft=activation.RightToLeft=L.Direction;
            target.AccessibleName=L.Text("موضع التصويب");
            activation.AccessibleName=L.Text("Hold Key:");
            AccessibleName=L.ProductName+" "+L.Text("Dashboard");
            Invalidate();
        }

        public void RefreshValues()
        {
            int region=Array.IndexOf(QuickActions.Points,options.Aim.AimPoint);
            if(region>=0&&region!=target.SelectedIndex)target.SelectedIndex=region;
            int key=Array.IndexOf(HoldKeys,options.Aim.HoldKey);
            if(key>=0&&key!=activation.SelectedIndex)activation.SelectedIndex=key;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            float sx=Width/1280f,sy=Height/740f;
            target.Bounds=new Rectangle((int)(860*sx),(int)(626*sy),(int)(366*sx),Math.Max(23,(int)(38*sy)));
            activation.Bounds=new Rectangle((int)(860*sx),(int)(671*sy),(int)(366*sx),Math.Max(23,(int)(38*sy)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics;
            SpectraTheme.PaintBackground(e,SpectraTheme.Background);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(Width/1280f,Height/740f);
            Frame current=frame();
            bool fresh=Painter.Fresh(current);
            SpectraTheme.Card(g,new RectangleF(19,10,1242,103),true);
            using(Brush dot=new SolidBrush(fresh?SpectraTheme.Green:SpectraTheme.Muted))g.FillEllipse(dot,44,36,10,10);
            SpectraTheme.Text(g,state(),65,22,252,34,23,fresh?SpectraTheme.Green:Color.White,true);
            SpectraTheme.Text(g,emulator()+"  |  "+resolution(),44,58,335,34,19,SpectraTheme.Muted);
            SpectraTheme.Text(g,L.Format("Reader: {0}",fresh&&current.ReadHertz>0?current.ReadHertz.ToString("0",L.Culture)+" Hz":"—"),
                419,23,350,34,19,SpectraTheme.Accent);
            SpectraTheme.Text(g,licenseState(),419,57,350,34,17,SpectraTheme.Muted);
            panelVisibility.Draw(g);
            SpectraTheme.Button(g,StartButton,L.Text("START"),canStart());

            Card(g,new Rectangle(19,130,600,275),L.Text("Player ESP"));
            Card(g,new Rectangle(651,130,610,275),L.Text("Item & Loot ESP"));
            foreach(ToggleTile tile in playerTiles)tile.Draw(g);
            foreach(ToggleTile tile in lootTiles)tile.Draw(g);
            lootMaster.Draw(g);
            SpectraTheme.Text(g,L.Text("المسافة")+"  "+options.MaxDistance.ToString("0",L.Culture)+" m",45,331,386,29,19,Color.White);
            Track(g,DistanceTrack,(options.MaxDistance-10)/4990f);
            SpectraTheme.Card(g,ColorPicker,false,255,8);
            using(Brush color=new SolidBrush(Color.FromArgb(options.Players.ColorArgb)))g.FillEllipse(color,ColorPicker.Right-31,ColorPicker.Y+10,20,20);
            SpectraTheme.Text(g,L.Text("Color:"),ColorPicker.X+11,ColorPicker.Y,65,40,16,Color.White);

            Card(g,new Rectangle(19,424,600,304),L.Text("Nearby count"));
            SpectraTheme.NearbyGraphic(g,new RectangleF(55,484,258,220));
            string count=fresh&&options.EspEnabled&&options.PlayerCountVisible?NearbyPlayers.Count(current,options).ToString(L.Culture):"—";
            SpectraTheme.Text(g,count,350,527,190,93,67,SpectraTheme.Accent,true);
            if(!fresh)SpectraTheme.Text(g,L.Text("Waiting for game data"),350,617,233,44,18,SpectraTheme.Muted);

            Card(g,new Rectangle(651,424,610,304),L.Text("مساعدة التصويب"));
            aimMaster.Draw(g);
            SpectraTheme.Text(g,L.Text("FOV radius"),679,494,395,34,20,Color.White);
            SpectraTheme.Text(g,options.Aim.Radius+" px",1110,494,115,34,20,SpectraTheme.Accent,true);
            Track(g,RadiusTrack,(options.Aim.Radius-20)/1180f);
            SpectraTheme.Text(g,L.Text("Strength"),679,551,395,34,20,Color.White);
            SpectraTheme.Text(g,options.Aim.Strength+"%",1110,551,115,34,20,SpectraTheme.Accent,true);
            Track(g,StrengthTrack,options.Aim.Strength/100f);
            SpectraTheme.Text(g,L.Text("Aim point:"),679,626,169,38,19,Color.White);
            SpectraTheme.Text(g,L.Text("Hold Key:"),679,671,169,38,19,Color.White);
        }

        private static void Card(Graphics g,Rectangle r,string heading)
        {
            SpectraTheme.Card(g,r,true);
            SpectraTheme.Text(g,heading,r.X+24,r.Y+9,r.Width-278,42,25,Color.White,true);
            using(Pen divider=new Pen(SpectraTheme.Border))g.DrawLine(divider,r.X+24,r.Y+53,r.Right-24,r.Y+53);
        }

        private static void Track(Graphics g,Rectangle r,float fraction)
        {
            float y=r.Y+r.Height/2f,x=r.X+r.Width*Math.Max(0,Math.Min(1,fraction));
            using(Pen track=new Pen(SpectraTheme.Border,5)) { track.StartCap=track.EndCap=LineCap.Round;g.DrawLine(track,r.X,y,r.Right,y); }
            using(Pen fill=new Pen(SpectraTheme.Accent,5)) { fill.StartCap=fill.EndCap=LineCap.Round;g.DrawLine(fill,r.X,y,x,y); }
            using(Brush glow=new SolidBrush(Color.FromArgb(47,SpectraTheme.Accent)))g.FillEllipse(glow,x-13,y-13,26,26);
            using(Brush knob=new SolidBrush(SpectraTheme.Accent))g.FillEllipse(knob,x-8,y-8,16,16);
        }
    }
    public sealed partial class MainForm
    {
        private Panel shellContent;
        private Control connectionPage;
        private SpectraDashboard dashboard;
        private OptionsForm optionsFactory;
        private Control activeSettingsPage;
        private int activeShellPage;
        private DateTime nextShellRefresh;
        private readonly SpectraChoice languageChoice = new SpectraChoice();
        private readonly System.Collections.Generic.List<Button> shellNavigation=new System.Collections.Generic.List<Button>();
        private void BuildSpectraShell()
        {
            SuspendLayout();connectionPage=CreateConnectionPage();
            Text=L.ProductName;FormBorderStyle=FormBorderStyle.None;MinimumSize=new Size(1120,720);Size=new Size(1440,820);Padding=new Padding(12);DoubleBuffered=true;MaximizedBounds=Screen.FromHandle(Handle).WorkingArea;
            TableLayoutPanel shell=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,RowCount=3,BackColor=Background,Margin=Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,230));shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,48));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));shell.RowStyles.Add(new RowStyle(SizeType.Absolute,62));Controls.Add(shell);
            SpectraSurface sidebar=new SpectraSurface { Dock=DockStyle.Fill,Padding=new Padding(15,12,15,0) };shell.Controls.Add(sidebar,0,0);shell.SetRowSpan(sidebar,3);
            sidebar.Paint+=delegate(object sender,PaintEventArgs e) { SpectraTheme.Wordmark(e.Graphics,new RectangleF(48,17,132,86),true); };
            string[] titles={"Dashboard","Player ESP","مساعدة التصويب","Visuals","Settings","Shortcuts","About"};
            string[] icons={"⌂","⊕","➶","☷","⚙","⌨","ⓘ"};
            for(int i=0;i<titles.Length;i++)
            {
                int page=i;Button button=new SpectraNavButton { Glyph=icons[i],Bounds=new Rectangle(14,120+i*58,200,49),FlatStyle=FlatStyle.Flat,ForeColor=SpectraTheme.Muted,BackColor=Background,Font=new Font("Segoe UI",13),Cursor=Cursors.Hand };
                button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=Surface;button.Click+=delegate { NavigateShell(page); };sidebar.Controls.Add(button);shellNavigation.Add(button);
                BindText(button,titles[i]);
            }
            Button support=SupportLink.CreateButton();
            support.Bounds=new Rectangle(14,sidebar.ClientSize.Height-57,200,41);
            support.Anchor=AnchorStyles.Left|AnchorStyles.Bottom;
            BindText(support,"Telegram support");
            sidebar.Controls.Add(support);
            SpectraSurface top=new SpectraSurface { Dock=DockStyle.Fill };shell.Controls.Add(top,1,0);
            FlowLayoutPanel windowButtons=new FlowLayoutPanel { Dock=DockStyle.Right,Width=150,WrapContents=false };
            string[] windowLabels={"—","□","×"};for(int i=0;i<3;i++) { int index=i;Button b=new Button { Text=windowLabels[i],Width=46,Height=34,FlatStyle=FlatStyle.Flat,BackColor=Background,ForeColor=Color.Gainsboro,Font=new Font("Segoe UI",16),TabStop=false };b.AccessibleName=L.Text(new[]{"Minimize","Maximize or restore","Close"}[i]);b.FlatAppearance.BorderSize=0;b.Click+=delegate { if(index==0)WindowState=FormWindowState.Minimized;else if(index==1)WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;else Close(); };windowButtons.Controls.Add(b); }top.Controls.Add(windowButtons);
            top.MouseDown+=delegate(object sender,MouseEventArgs e) { if(e.Button==MouseButtons.Left)SpectraTheme.DragWindow(Handle); };top.DoubleClick+=delegate { WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized; };
            shellContent=new Panel { Dock=DockStyle.Fill,Margin=new Padding(4,0,4,10),BackColor=Background };shell.Controls.Add(shellContent,1,1);
            SpectraSurface footer=new SpectraSurface { Dock=DockStyle.Fill,Margin=new Padding(4,0,4,4) };shell.Controls.Add(footer,1,2);
            footer.Paint+=delegate(object sender,PaintEventArgs e) {
                SpectraTheme.Card(e.Graphics,new RectangleF(0,0,Math.Max(2,footer.Width-2),Math.Max(2,footer.Height-2)));
                SpectraTheme.Text(e.Graphics,status.Text,18,5,Math.Max(50,footer.Width-36),40,15,SpectraTheme.Muted);
            };
            dashboard=new SpectraDashboard(options,ChangedQuickOptions,async delegate { await StartFromHome(); },
                ()=>Emulators.Name(activeEmulator),()=>{ Instance selected=instances.SelectedItem as Instance;Rectangle r=selected==null?Rectangle.Empty:Native.ClientBounds(selected.Window);return r.Width>0?r.Width+" × "+r.Height:"—"; },
                ()=>L.Text(liveDisplay?(overlay.Suppressed?"Paused":"LIVE"):demoMode?"DEMO":busy?"Working":"Idle"),()=>live.Enabled,
                ()=>latest,DashboardLicenseState,()=>overlay.Quick.IsVisible(options,overlay.Suppressed),ToggleDashboardPanel);
            timer.Tick+=delegate {
                if(DateTime.UtcNow<nextShellRefresh)return;nextShellRefresh=DateTime.UtcNow.AddMilliseconds(250);
                if(dashboard.Visible)dashboard.Invalidate();footer.Invalidate();
            };
            NavigateShell(0);ResumeLayout(true);
            FormClosed+=delegate { if(optionsFactory!=null)optionsFactory.Dispose(); };
        }
        private string DashboardLicenseState()
        {
#if DISTRIBUTION
            return license==null?"":license.Description;
#else
            return L.Text("PUBG Mobile Assistant");
#endif
        }
        private void ToggleDashboardPanel()
        {
            if(overlay.Quick.IsVisible(options,overlay.Suppressed))overlay.Quick.Hidden=true;
            else overlay.Quick.ToggleCollapse(options,overlay.Suppressed);
            overlay.RefreshQuickPanel();
        }
        private void NavigateShell(int page)
        {
            if(page<0||page>=shellNavigation.Count)return;
            shellContent.Controls.Clear();
            if(activeSettingsPage!=null) { activeSettingsPage.Dispose();activeSettingsPage=null; }
            activeShellPage=page;
            for(int i=0;i<shellNavigation.Count;i++) { shellNavigation[i].BackColor=i==page?SpectraTheme.Selected:Background;shellNavigation[i].ForeColor=i==page?SpectraTheme.Accent:SpectraTheme.Muted; }
            if(page==0) { dashboard.RefreshValues();shellContent.Controls.Add(dashboard); }
            else if(page==4) { shellContent.Controls.Add(connectionPage); }
            else if(page==6)
            {
                activeSettingsPage=CreateAboutPage();shellContent.Controls.Add(activeSettingsPage);
            }
            else
            {
                if(optionsFactory==null)optionsFactory=new OptionsForm(options,SaveOptions,CaptureShortcuts,ChangedQuickOptions,previewOnly?(Func<bool,string>)null:ApplyImageQuality);
                activeSettingsPage=page==1?CreateEspPage():optionsFactory.CreateEmbeddedPage(page==2?1:page==3?7:8);
                shellContent.Controls.Add(activeSettingsPage);
            }
            shellContent.PerformLayout();shellContent.Invalidate(true);
        }
        private Control CreateEspPage()
        {
            Panel page=new Panel { Dock=DockStyle.Fill,BackColor=Background };
            FlowLayoutPanel tabs=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(12,0,10,8),BackColor=Background,Name="EspTabs" };
            Panel body=new Panel { Dock=DockStyle.Fill,BackColor=Background };
            page.Controls.Add(body);page.Controls.Add(tabs);
            string[] titles={L.Text("General"),L.Text("اللاعبون والبوتات"),L.Text("أهداف التدريب"),L.Text("المركبات"),L.Text("العناصر والأسلحة"),L.Text("الصناديق")};int[] indices={0,2,3,4,5,6};
            Action<int> show=delegate(int index) {
                while(body.Controls.Count>0)body.Controls[0].Dispose();body.Controls.Add(optionsFactory.CreateEmbeddedPage(indices[index]));
                for(int i=0;i<tabs.Controls.Count;i++)((SpectraButton)tabs.Controls[i]).Highlighted=i==index;
            };
            for(int i=0;i<titles.Length;i++) { int index=i;Button tab=new SpectraButton { Text=titles[i],RightToLeft=L.Direction,AutoSize=true,MinimumSize=new Size(88,38),Padding=new Padding(9,1,9,1),Margin=new Padding(0,0,4,0) };tab.Click+=delegate { show(index); };tabs.Controls.Add(tab); }
            show(0);return page;
        }
        private Control CreateConnectionPage()
        {
            languageChoice.Items.AddRange(new object[]{"English","العربية","Deutsch","Français"});
            languageChoice.SelectedIndex=L.Index;
            languageChoice.SelectedIndexChanged+=delegate { if(languageChoice.SelectedIndex!=L.Index)ChangeLanguage(languageChoice.SelectedIndex); };
            Label language=SpectraFields.Caption("");
            BindText(language,"Language");
            var page=new SpectraPage(L.Text("Connection & diagnostics"),null,SpectraFields.Pair(language,languageChoice,139,36));
            BindText(page.Heading,"Connection & diagnostics");

            var tabs=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,0,0,8) };
            page.AddGroup(tabs,0);
            var setup=page.Group(L.Text("Emulator connection"));
            BindText(setup.Heading,"Emulator connection");
            connectionSetup=setup;
            emulators.Items.AddRange(new object[]{"LDPlayer 14","GameLoop"});
            AddConnectionField(setup,"Emulator",emulators);
            AddPath(setup,"ADB executable",adbPath,delegate { BrowseExecutable(adbPath,"adb.exe"); });
            consoleRow=AddPath(setup,"LDPlayer console",consolePath,delegate { BrowseExecutable(consolePath,"ldconsole.exe"); });
            endpointRow=AddPath(setup,"GameLoop ADB address",endpoint,null);
            AddConnectionField(setup,"ADB device (actual serial)",devices);
            AddConnectionField(setup,"Overlay target (select matching instance)",instances);
            var tools=new FlowLayoutPanel { WrapContents=true,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,5,0,0) };
            ConfigureButton(scan,"تحديث الأجهزة",async delegate { await Scan(); });
            ConfigureButton(inspect,"فحص اللعبة",async delegate { await Inspect(); });
            tools.Controls.AddRange(new Control[]{scan,inspect});
            setup.Add(tools);
            AddConnectionNote(setup,"التشخيص يقرأ بيانات الاتصال والذاكرة فقط.");

            SpectraCard profile=page.Group(L.Text("Profile & preview"));
            BindText(profile.Heading,"Profile & preview");
            profileRow=AddPath(profile,"JSON profile (optional)",profilePath,delegate {
                using(var dialog=new OpenFileDialog { Filter=L.Text("JSON profile (*.json)|*.json"),InitialDirectory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles") })
                    if(dialog.ShowDialog(this)==DialogResult.OK)profilePath.Text=dialog.FileName;
            });
            AddConnectionNote(profile,"Leave the profile blank to use the verified profile for the installed game build.");
            AddConnectionNote(profile,"Live يحتاج ملف إزاحات متحقق من هذا الإصدار.");
            AddProfileSwitch(profile,"Boxes",()=>options.Boxes,v=>options.Boxes=v);
            AddProfileSwitch(profile,"الصحة",()=>options.Health,v=>options.Health=v);
            AddProfileSwitch(profile,"Labels",()=>options.Names,v=>options.Names=v);
            AddProfileSwitch(profile,"المسافة",()=>options.Distance,v=>options.Distance=v);
            AddProfileSwitch(profile,"Top lines",()=>options.Lines,v=>options.Lines=v);
            AddProfileSwitch(profile,"Alerts",()=>options.Alerts,v=>options.Alerts=v);
            AddProfileSwitch(profile,"أفراد الفريق",()=>options.Allies,v=>options.Allies=v,true);
            Label distanceLabel=SpectraFields.Caption("");
            BindText(distanceLabel,"Max distance (m)");
            var distance=new SpectraNumber((decimal)options.MaxDistance,10,5000,1,distanceLabel.Text);
            distance.ValueChanged+=delegate { options.MaxDistance=(float)distance.Value;ChangedQuickOptions(); };
            profile.VisibleChanged+=delegate { if(profile.Visible&&distance.Value!=(decimal)options.MaxDistance)distance.Value=(decimal)options.MaxDistance; };
            profile.Add(SpectraFields.Pair(distanceLabel,distance,144,38));

            SpectraButton emulatorTab=new SpectraButton { AutoSize=true,MinimumSize=new Size(120,38),Padding=new Padding(10,0,10,0),Margin=new Padding(0,0,4,0) };
            SpectraButton profileTab=new SpectraButton { AutoSize=true,MinimumSize=new Size(120,38),Padding=new Padding(10,0,10,0),Margin=Padding.Empty,Name="ProfileTab" };
            BindText(emulatorTab,"Emulator connection");
            BindText(profileTab,"Profile & preview");
            Action<bool> select=delegate(bool showProfile) {
                setup.Visible=!showProfile;
                profile.Visible=showProfile;
                emulatorTab.Highlighted=!showProfile;
                profileTab.Highlighted=showProfile;
            };
            emulatorTab.Click+=delegate { select(false); };
            profileTab.Click+=delegate { select(true); };
            tabs.Controls.AddRange(new Control[]{emulatorTab,profileTab});
            select(false);

            SpectraCard actions=page.Group(L.Text("Session and panel"),1);
            BindText(actions.Heading,"Session and panel");
            var actionRow=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=Padding.Empty };
            ConfigureButton(live,"تشغيل Live",async delegate { await StartFromHome(); });
            ((SpectraButton)live).Highlighted=true;
            ConfigureButton(demo,"عرض تجريبي",delegate { StartDemo(); });
            ConfigureButton(stop,"إيقاف",async delegate { await StopReader(); });
            Button menu=new SpectraButton();
            ConfigureButton(menu,"القائمة الكاملة",delegate { OpenMenu(); });
            Button administrator=new SpectraButton();
            ConfigureButton(administrator,"تشغيل كمسؤول",delegate { RestartAsAdministrator(); });
            actionRow.Controls.AddRange(new Control[]{live,demo,stop,menu,administrator});
            actions.Add(actionRow);
            AddConnectionNote(actions,"Insert: restore, collapse or expand panel");

            SpectraCard preview=page.Group(L.Text("Preview"),1);
            BindText(preview.Heading,"Preview");
            scene.Height=168;
            scene.Margin=new Padding(1,0,1,0);
            preview.Add(scene);
            SpectraCard details=page.Group(L.Text("Activity log"),1);
            BindText(details.Heading,"Activity log");
            log.Height=125;
            log.Margin=new Padding(1,0,1,0);
            details.Add(log);
            return page;
        }

        private void AddConnectionNote(SpectraCard card,string key)
        {
            Label note=SpectraFields.Note("");
            BindText(note,key);
            card.Add(note);
        }

        private Control AddConnectionField(SpectraCard card,string key,Control input)
        {
            var field=new SpectraStack { Margin=new Padding(0,2,0,5) };
            Label label=SpectraFields.Caption("");
            label.Height=24;
            BindText(label,key);
            field.Add(label);
            input.Height=35;
            input.Margin=Padding.Empty;
            if(input is SpectraChoice)input.AccessibleName=label.Text;
            field.Add(input);
            card.Add(field);
            return field;
        }

        private Control AddPath(SpectraCard card,string key,TextBox input,Action browse)
        {
            input.AccessibleName=L.Text(key);
            input.RightToLeft=RightToLeft.No;
            Panel frame=SpectraFields.TextField(input);
            if(browse==null)return AddConnectionField(card,key,frame);
            Button button=new SpectraButton { Text="…",AccessibleName=L.Text("Browse") };
            button.Click+=delegate { browse(); };
            return AddConnectionField(card,key,SpectraFields.Pair(frame,button,39,35));
        }

        private void AddProfileSwitch(SpectraCard card,string key,Func<bool> current,Action<bool> set,bool developing=false)
        {
            var toggle=new SpectraSwitch { Checked=current(),Height=32,Margin=Padding.Empty };
            BindText(toggle,key,developing);
            toggle.CheckedChanged+=delegate { if(toggle.Checked!=current()) { set(toggle.Checked);ChangedQuickOptions(); } };
            toggle.VisibleChanged+=delegate { if(toggle.Visible)toggle.Checked=current(); };
            card.Add(toggle);
        }

        private Control CreateAboutPage()
        {
            var page=new SpectraPage(L.Text("About"));
            SpectraCard info=page.Group(L.ProductName);
            var logo=new SpectraSurface { Height=119,BackColor=info.BackColor };
            logo.Paint+=delegate(object sender,PaintEventArgs e) {
                float width=Math.Min(152,logo.Width-20);
                if(width>0)SpectraTheme.Wordmark(e.Graphics,new RectangleF((logo.Width-width)/2,6,width,102),true);
            };
            info.Add(logo);
            Label product=SpectraFields.Caption(L.Text("PUBG Mobile Assistant"));
            product.Height=29;product.TextAlign=ContentAlignment.MiddleCenter;
            info.Add(product);
            Label emulators=SpectraFields.Caption("LDPlayer 14  /  GameLoop");
            emulators.Height=30;emulators.TextAlign=ContentAlignment.MiddleCenter;
            info.Add(emulators);
#if DISTRIBUTION
            info.Add(SpectraFields.Note(Distribution.VersionText));
            if(license!=null)info.Add(SpectraFields.Note(license.Description));
#endif
            SpectraCard usage=page.Group(L.Text("In-game panel"),1);
            usage.Add(SpectraFields.Note(L.Text("Insert: restore, collapse or expand panel")));
            Button shortcuts=new SpectraButton { Text=L.Text("Keyboard shortcuts"),Height=39,Margin=new Padding(1,2,1,0) };
            shortcuts.Click+=delegate { NavigateShell(5); };
            usage.Add(shortcuts);
            return page;
        }

        public void RenderShell(string directory)
        {
            System.IO.Directory.CreateDirectory(directory);ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();Application.DoEvents();
            for(int i=0;i<shellNavigation.Count;i++)
            {
                NavigateShell(i);
                SaveShellPreview(directory,"page-"+i);
                if(i==1)
                {
                    Control tabs=shellContent.Controls.Find("EspTabs",true)[0];
                    for(int j=1;j<tabs.Controls.Count;j++)
                    {
                        ((Button)tabs.Controls[j]).PerformClick();
                        SaveShellPreview(directory,"esp-"+j);
                    }
                }
                if(i==4)
                {
                    ((Button)shellContent.Controls.Find("ProfileTab",true)[0]).PerformClick();
                    SaveShellPreview(directory,"settings-profile");
                }
            }
            Hide();
        }
        private void SaveShellPreview(string directory,string name)
        {
            Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(Width,Height))
            {
                DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));
                bitmap.Save(Path.Combine(directory,name+".png"),System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            RectangleF bounds=new RectangleF(2,2,Width-5,Height-5);
            using(GraphicsPath border=SpectraTheme.Round(bounds,16))SpectraTheme.NeonOutline(e.Graphics,border,bounds);
        }
        public void RenderHome(string path)
        {
            // Render without connecting to a running emulator or starting a session.
            ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);Show();Application.DoEvents();
            using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
            Hide();
        }
    }
}
