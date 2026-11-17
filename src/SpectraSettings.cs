using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal class SpectraButton : Button
    {
        private bool hover;
        private bool highlighted;
        internal bool Highlighted
        {
            get { return highlighted; }
            set { if(highlighted!=value) { highlighted=value;Invalidate(); } }
        }
        internal Color Swatch { get; set; }

        internal SpectraButton()
        {
            FlatStyle=FlatStyle.Flat;
            Cursor=Cursors.Hand;
            ForeColor=Color.White;
            DoubleBuffered=true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            SpectraTheme.PaintBackground(e,Parent==null?SpectraTheme.Surface:Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            var bounds=new RectangleF(3,3,Width-6,Height-6);
            if(bounds.Width<=0||bounds.Height<=0)return;
            if(Highlighted||Focused||hover)SpectraTheme.Card(e.Graphics,bounds,Enabled,255,8);
            else SpectraTheme.Card(e.Graphics,bounds,false,255,8);
            int inset=Width<52?3:8;
            var label=new Rectangle(inset,0,Width-inset*2,Height);
            if(!Swatch.IsEmpty)
            {
                using(var color=new SolidBrush(Swatch))e.Graphics.FillEllipse(color,12,(Height-14)/2,14,14);
                label.X+=18;
                label.Width-=18;
            }
            SpectraTheme.DrawCaption(e.Graphics,Text,Font,label,Enabled?(Highlighted?SpectraTheme.Accent:ForeColor):SpectraTheme.Muted,
                TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e);hover=true;Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e);hover=false;Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e);Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e);Invalidate(); }
    }

    internal class SpectraStack : Panel
    {
        internal SpectraStack()
        {
            Margin=Padding.Empty;
        }

        internal void Add(Control control)
        {
            control.Dock=DockStyle.None;
            Controls.Add(control);
            control.VisibleChanged+=ContentChanged;
            control.TextChanged+=ContentChanged;
            control.RightToLeftChanged+=ContentChanged;
            if(control is FlowLayoutPanel)
            {
                var flow=(FlowLayoutPanel)control;
                flow.AutoSize=false;
                flow.ControlAdded+=delegate(object sender,ControlEventArgs e) { WatchFlowItem(e.Control);RequestLayout(); };
                flow.ControlRemoved+=delegate(object sender,ControlEventArgs e) { e.Control.VisibleChanged-=ContentChanged;RequestLayout(); };
                foreach(Control item in flow.Controls)WatchFlowItem(item);
            }
            RequestLayout();
        }

        private void WatchFlowItem(Control control)
        {
            control.VisibleChanged+=ContentChanged;
        }

        private void ContentChanged(object sender,EventArgs e) { RequestLayout(); }

        private void RequestLayout()
        {
            for(Control parent=Parent;parent!=null;parent=parent.Parent)
            {
                var page=parent as SpectraScroll;
                if(page==null)continue;
                page.Reflow();
                break;
            }
        }

        internal int Arrange(int width)
        {
            int y=Padding.Top;
            foreach(Control child in Controls)
            {
                if(!child.Visible)continue;
                int available=Math.Max(1,width-Padding.Horizontal-child.Margin.Horizontal);
                int height=child.Height;
                var stack=child as SpectraStack;
                var grid=child as SpectraColumns;
                var toggle=child as SpectraSwitch;
                var pair=child as SpectraFieldRow;
                if(stack!=null)height=stack.Arrange(available);
                else if(grid!=null)height=grid.Arrange(available);
                else if(toggle!=null)height=toggle.PreferredHeight(available);
                else if(pair!=null)height=pair.PreferredHeight(available);
                else if(child is SpectraNote||child is FlowLayoutPanel)
                    height=child.GetPreferredSize(new Size(available,0)).Height;
                y+=child.Margin.Top;
                child.SetBounds(Padding.Left+child.Margin.Left,y,available,height);
                y+=height+child.Margin.Bottom;
            }
            return y+Padding.Bottom;
        }
    }

    internal sealed class SpectraColumns : Panel
    {
        private readonly SpectraStack first,second;

        internal SpectraColumns(SpectraStack first,SpectraStack second)
        {
            this.first=first;this.second=second;
            Margin=Padding.Empty;
            Controls.Add(first);
            Controls.Add(second);
        }

        internal int Arrange(int width)
        {
            bool two=width>=704;
            int leftWidth=two?(width-12)/2:width;
            int rightWidth=two?width-12-leftWidth:width;
            int firstHeight=first.Arrange(leftWidth);
            int secondHeight=second.Arrange(rightWidth);
            first.SetBounds(0,0,leftWidth,firstHeight);
            second.SetBounds(two?leftWidth+12:0,two?0:firstHeight,rightWidth,secondHeight);
            return two?Math.Max(firstHeight,secondHeight):firstHeight+secondHeight;
        }
    }

    internal sealed class SpectraCard : SpectraStack
    {
        internal Label Heading { get; private set; }
        internal SpectraCard(string title)
        {
            BackColor=SpectraTheme.Surface;
            ForeColor=Color.White;
            Padding=new Padding(15,11,15,13);
            SetStyle(ControlStyles.OptimizedDoubleBuffer,true);
            if(!String.IsNullOrEmpty(title))
            {
                Heading=new Label {
                    Text=title,Height=35,RightToLeft=L.Direction,TextAlign=ContentAlignment.MiddleLeft,UseMnemonic=false,
                    Font=new Font("Segoe UI Semibold",13),ForeColor=Color.White,Margin=new Padding(1,0,1,4)
                };
                Add(Heading);
                Add(new Panel { Height=1,BackColor=SpectraTheme.Border,Margin=new Padding(1,0,1,8) });
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            SpectraTheme.PaintBackground(e,Parent==null?SpectraTheme.Background:Parent.BackColor);
            if(Width<8||Height<8)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            SpectraTheme.Card(e.Graphics,new RectangleF(3,3,Width-6,Height-6),true,255,12,BackColor);
        }
    }

    internal sealed class SpectraScroll : Panel
    {
        private readonly Panel clip=new Panel { Dock=DockStyle.Fill };
        private readonly Panel scroll=new Panel { AutoScroll=true };
        private readonly Panel track=new Panel { Dock=DockStyle.Right,Width=SystemInformation.VerticalScrollBarWidth+4,Cursor=Cursors.Hand };
        private readonly SpectraStack content;
        private int dragOffset=-1;
        private bool arranging;

        internal SpectraScroll(SpectraStack content)
        {
            this.content=content;
            Dock=DockStyle.Fill;
            BackColor=SpectraTheme.Background;
            Margin=Padding.Empty;
            Controls.Add(clip);
            Controls.Add(track);
            clip.Controls.Add(scroll);
            content.Dock=DockStyle.None;
            scroll.Controls.Add(content);
            clip.SizeChanged+=delegate { Reflow(); };
            content.LocationChanged+=delegate {
                clip.Invalidate(true);
                track.Invalidate();
            };
            scroll.Scroll+=delegate { track.Invalidate(); };
            track.Paint+=PaintTrack;
            track.MouseDown+=delegate(object sender,MouseEventArgs e) {
                Rectangle thumb=Thumb();
                if(e.Button!=MouseButtons.Left||thumb.IsEmpty)return;
                dragOffset=thumb.Contains(e.Location)?e.Y-thumb.Y:thumb.Height/2;
                track.Capture=true;
                Drag(e.Y);
            };
            track.MouseMove+=delegate(object sender,MouseEventArgs e) { if(track.Capture&&dragOffset>=0)Drag(e.Y); };
            track.MouseUp+=delegate { track.Capture=false;dragOffset=-1; };
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if(Visible)Reflow();
        }

        internal void Reflow()
        {
            if(arranging||!Visible||clip.Width<=0||clip.Height<=0)return;
            arranging=true;
            scroll.SuspendLayout();
            try
            {
                scroll.Size=new Size(clip.Width+SystemInformation.VerticalScrollBarWidth,clip.Height);
                content.Size=new Size(clip.Width,content.Arrange(clip.Width));
            }
            finally
            {
                scroll.ResumeLayout(true);
                arranging=false;
            }
            clip.Invalidate(true);
            track.Invalidate();
        }

        private Rectangle Thumb()
        {
            int height=track.Height-10,range=content.Height-scroll.ClientSize.Height;
            if(height<20||range<=0)return Rectangle.Empty;
            int thumb=Math.Max(24,height*scroll.ClientSize.Height/content.Height);
            return new Rectangle(4,5+Math.Min(height-thumb,-scroll.AutoScrollPosition.Y*(height-thumb)/range),5,thumb);
        }

        private void Drag(int y)
        {
            Rectangle thumb=Thumb();
            int travel=track.Height-10-thumb.Height;
            if(thumb.IsEmpty||travel<=0)return;
            double fraction=Math.Max(0,Math.Min(1,(y-5-dragOffset)/(double)travel));
            scroll.AutoScrollPosition=new Point(0,(int)Math.Round(fraction*(content.Height-scroll.ClientSize.Height)));
            track.Invalidate();
        }

        private void PaintTrack(object sender,PaintEventArgs e)
        {
            Rectangle thumb=Thumb();
            if(thumb.IsEmpty)return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var channel=new SolidBrush(SpectraTheme.Surface))e.Graphics.FillRectangle(channel,4,5,5,track.Height-10);
            using(var fill=new SolidBrush(SpectraTheme.Accent))
            using(var path=SpectraTheme.Round(thumb,2.5f))e.Graphics.FillPath(fill,path);
        }
    }

    internal sealed class SpectraPage : Panel
    {
        private readonly SpectraStack[] columns={new SpectraStack(),new SpectraStack()};
        internal Label Heading { get; private set; }

        internal SpectraPage(string title,string description=null,Control headerAction=null)
        {
            Dock=DockStyle.Fill;
            BackColor=SpectraTheme.Background;
            SpectraStack body=new SpectraStack { Padding=new Padding(12,4,4,5),BackColor=BackColor };
            SpectraCard header=new SpectraCard(null) { Margin=new Padding(0,0,0,12) };
            Heading=new Label { Text=title,Height=37,Font=new Font("Segoe UI Semibold",19),ForeColor=SpectraTheme.Accent,UseMnemonic=false,
                TextAlign=ContentAlignment.MiddleLeft,RightToLeft=L.Direction };
            if(headerAction==null)header.Add(Heading);
            else header.Add(SpectraFields.Pair(Heading,headerAction,270,39));
            if(!String.IsNullOrEmpty(description))header.Add(SpectraFields.Note(description));
            body.Add(header);
            body.Add(new SpectraColumns(columns[0],columns[1]));
            Controls.Add(new SpectraScroll(body));
        }

        internal SpectraCard Group(string title,int column=0)
        {
            SpectraCard card=new SpectraCard(title) { Margin=new Padding(0,0,0,12) };
            columns[column].Add(card);
            return card;
        }

        internal void AddGroup(Control group,int column)
        {
            columns[column].Add(group);
        }
    }

    internal sealed class SpectraFieldRow : TableLayoutPanel
    {
        private readonly Control label,input;
        private readonly int inputWidth,minimumHeight;

        internal SpectraFieldRow(Control label,Control input,int inputWidth,int height)
        {
            this.label=label;this.input=input;this.inputWidth=inputWidth;minimumHeight=height;
            ColumnCount=2;RowCount=1;Height=height;Margin=new Padding(0,2,0,2);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,inputWidth));
            label.Dock=input.Dock=DockStyle.Fill;
            label.Margin=new Padding(2,0,8,0);
            input.Margin=Padding.Empty;
            Controls.Add(label,0,0);
            Controls.Add(input,1,0);
        }

        internal int PreferredHeight(int width)
        {
            int fieldWidth=Math.Min(inputWidth,Math.Max(1,width*2/3));
            if(ColumnStyles[1].Width!=fieldWidth)ColumnStyles[1].Width=fieldWidth;
            int height=minimumHeight;
            if(label is Label)
            {
                int textWidth=Math.Max(1,width-fieldWidth-label.Margin.Horizontal);
                int measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(textWidth,Int32.MaxValue),
                    TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
                height=Math.Max(height,measured+4);
            }
            var choice=input as SpectraChoice;
            return choice==null?height:Math.Max(height,choice.PreferredHeight(fieldWidth));
        }
    }

    internal sealed class SpectraNote : Label
    {
        public override Size GetPreferredSize(Size proposedSize)
        {
            return TextRenderer.MeasureText(Text,Font,new Size(Math.Max(1,proposedSize.Width),int.MaxValue),
                TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak);
        }
    }

    internal static class SpectraFields
    {
        internal static Label Note(string text)
        {
            return new SpectraNote { Text=text,UseMnemonic=false,RightToLeft=L.Direction,ForeColor=SpectraTheme.Muted,
                Margin=new Padding(2,3,2,7) };
        }

        internal static Label Caption(string text)
        {
            return new Label { Text=text,UseMnemonic=false,RightToLeft=L.Direction,
                ForeColor=Color.Gainsboro,TextAlign=ContentAlignment.MiddleLeft };
        }

        internal static TableLayoutPanel Pair(Control label,Control control,int width,int height)
        {
            return new SpectraFieldRow(label,control,width,height);
        }

        internal static Panel TextField(TextBox input)
        {
            var frame=new Panel { Height=34,BackColor=SpectraTheme.Background,Padding=new Padding(9,8,9,4),Margin=Padding.Empty };
            frame.Paint+=delegate(object sender,PaintEventArgs e) {
                if(frame.Width<=2||frame.Height<=2)return;
                using(var pen=new Pen(input.Focused?SpectraTheme.Accent:SpectraTheme.Border))
                    e.Graphics.DrawRectangle(pen,0,0,frame.Width-1,frame.Height-1);
            };
            input.BorderStyle=BorderStyle.None;
            input.BackColor=SpectraTheme.Background;
            input.ForeColor=Color.White;
            input.Dock=DockStyle.Top;
            input.GotFocus+=delegate { frame.Invalidate(); };
            input.LostFocus+=delegate { frame.Invalidate(); };
            frame.Controls.Add(input);
            return frame;
        }
    }

    internal sealed class SpectraNumber : Panel
    {
        private readonly TextBox input=new TextBox { TextAlign=HorizontalAlignment.Center };
        private readonly decimal min,max,step;
        private decimal value;
        private readonly int decimalPlaces;
        private readonly string format;
        internal event EventHandler ValueChanged;
        internal decimal Value
        {
            get { return value; }
            set
            {
                decimal next=Math.Max(min,Math.Min(max,Decimal.Round(value,decimalPlaces)));
                bool changed=next!=this.value;
                this.value=next;
                input.Text=next.ToString(format,L.Culture);
                if(changed&&ValueChanged!=null)ValueChanged(this,EventArgs.Empty);
            }
        }

        internal SpectraNumber(decimal initial,decimal min,decimal max,decimal step,string label)
        {
            this.min=min;this.max=max;this.step=step;
            decimalPlaces=step<1?1:0;
            format=decimalPlaces==1?"0.0":"0";
            input.AccessibleName=label;
            Width=160;
            Height=34;
            Margin=Padding.Empty;
            var buttons=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,Margin=Padding.Empty };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,34));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,34));
            SpectraButton minus=new SpectraButton { Text="−",AccessibleName=L.Text("Decrease"),Dock=DockStyle.Fill,Margin=Padding.Empty };
            SpectraButton plus=new SpectraButton { Text="+",AccessibleName=L.Text("Increase"),Dock=DockStyle.Fill,Margin=Padding.Empty };
            minus.Click+=delegate { Adjust(-1); };
            plus.Click+=delegate { Adjust(1); };
            Panel field=SpectraFields.TextField(input);
            field.Dock=DockStyle.Fill;field.Margin=new Padding(2,3,2,3);field.Padding=new Padding(3,5,3,1);
            buttons.Controls.Add(minus,0,0);buttons.Controls.Add(field,1,0);buttons.Controls.Add(plus,2,0);
            Controls.Add(buttons);
            input.LostFocus+=delegate { Commit(); };
            input.KeyDown+=delegate(object sender,KeyEventArgs e) {
                if(e.KeyCode==Keys.Enter)Commit();
                else if(e.KeyCode==Keys.Up||e.KeyCode==Keys.Down)Adjust(e.KeyCode==Keys.Up?1:-1);
                else if(e.KeyCode==Keys.Escape)Value=value;
                else return;
                e.SuppressKeyPress=true;
            };
            Value=initial;
        }

        private void Commit()
        {
            decimal entered;
            Value=Decimal.TryParse(input.Text,NumberStyles.Number,L.Culture,out entered)?entered:value;
        }

        private void Adjust(int direction)
        {
            Commit();
            Value=value+direction*step;
        }
    }

    internal sealed class SpectraLog : TextBox
    {
        [DllImport("user32.dll",EntryPoint="SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
        private int wheel;

        internal SpectraLog()
        {
            Multiline=true;
            ReadOnly=true;
            ScrollBars=ScrollBars.None;
            BorderStyle=BorderStyle.None;
            BackColor=SpectraTheme.Background;
            ForeColor=SpectraTheme.Muted;
            Font=new Font("Consolas",9);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if(GetLineFromCharIndex(TextLength)+1<=ClientSize.Height/Font.Height)
            {
                base.OnMouseWheel(e);
                return;
            }
            wheel+=e.Delta;
            int steps=wheel/SystemInformation.MouseWheelScrollDelta;
            wheel%=SystemInformation.MouseWheelScrollDelta;
            int lines=SystemInformation.MouseWheelScrollLines;
            IntPtr command=new IntPtr(lines<0?(steps>0?2:3):(steps>0?0:1));
            int repetitions=Math.Abs(steps)*(lines<0?1:lines);
            for(int i=0;i<repetitions;i++)SendMessage(Handle,0x0115,command,IntPtr.Zero);
            HandledMouseEventArgs handled=e as HandledMouseEventArgs;
            if(handled!=null)handled.Handled=true;
        }
    }
}
