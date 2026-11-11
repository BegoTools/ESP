using System;
using System.Drawing;
using System.Windows.Forms;

namespace ESPWorkbench
{
    public sealed class ShortcutOptions
    {
        public static readonly string[] Names={"Pause overlay + input","Stop session","Quick adjustment panel","Aim Assist / input ON–OFF","Activation mode","Target body region","ESP ON–OFF","Full settings menu"};
        public int[] Bindings={ (int)Keys.F8,(int)Keys.F9,(int)Keys.F7,(int)Keys.F3,(int)Keys.F4,(int)Keys.F5,(int)Keys.F6,(int)(Keys.Control|Keys.F7) };
        public void EnsureDefaults()
        {
            if(Bindings==null||Bindings.Length!=8) { Bindings=new ShortcutOptions().Bindings;return; }
            for(int i=0;i<Bindings.Length;i++)
            {
                if(!Allowed((Keys)Bindings[i])) { Bindings=new ShortcutOptions().Bindings;return; }
                for(int j=0;j<i;j++)if(Bindings[i]==Bindings[j]) { Bindings=new ShortcutOptions().Bindings;return; }
            }
        }
        public static bool Allowed(Keys value)
        {
            Keys key=value&Keys.KeyCode;
            return key>=Keys.Back&&key<=Keys.OemClear&&key!=Keys.Insert&&key!=Keys.ControlKey&&key!=Keys.ShiftKey&&key!=Keys.Menu&&key!=Keys.LWin&&key!=Keys.RWin&&key!=Keys.LControlKey&&key!=Keys.RControlKey&&key!=Keys.LShiftKey&&key!=Keys.RShiftKey&&key!=Keys.LMenu&&key!=Keys.RMenu&&key!=Keys.Escape&&key!=Keys.Left&&key!=Keys.Right&&key!=Keys.Up&&key!=Keys.Down&&(value&~(Keys.KeyCode|Keys.Control|Keys.Alt|Keys.Shift))==0;
        }
        public bool TrySet(int index,Keys value,out string error)
        {
            error="";
            if(index<0||index>=8||!Allowed(value)) { error=L.Text("Choose another key. Insert is reserved for collapsing the overlay; arrows adjust it.");return false; }
            for(int i=0;i<8;i++)if(i!=index&&Bindings[i]==(int)value) { error=L.Format(L.Text("Already assigned to: {0}"),L.Text(Names[i]));return false; }
            Bindings[index]=(int)value;return true;
        }
        public Keys Key(int id) { return (Keys)Bindings[id-1]; }
        public string Label(int id) { return Format(Key(id)); }
        public static string Format(Keys value) { return new KeysConverter().ConvertToString(value); }
        public uint Modifiers(int id)
        {
            Keys value=Key(id);return 0x4000u|((value&Keys.Control)!=0?2u:0)|((value&Keys.Shift)!=0?4u:0)|((value&Keys.Alt)!=0?1u:0);
        }
    }
    internal sealed class ShortcutKeyButton : Button
    {
        private readonly ShortcutOptions shortcuts;
        private readonly int index;
        private readonly Action<bool> capture;
        private readonly Action changed;
        private readonly Label feedback;
        public bool Capturing { get;private set; }
        public ShortcutKeyButton(ShortcutOptions shortcuts,int index,Action<bool> capture,Action changed,Label feedback)
        {
            this.shortcuts=shortcuts;this.index=index;this.capture=capture;this.changed=changed;this.feedback=feedback;
            Text=shortcuts.Label(index+1);Width=230;Height=40;BackColor=SpectraTheme.Surface;ForeColor=Color.White;FlatStyle=FlatStyle.Flat;Cursor=Cursors.Hand;
            FlatAppearance.BorderColor=SpectraTheme.Border;
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);Focus();if(Capturing)return;Capturing=true;if(capture!=null)capture(true);
            Text=L.Text("Press a key…");FlatAppearance.BorderColor=SpectraTheme.Accent;feedback.ForeColor=SpectraTheme.Muted;feedback.Text=L.Text("Press your new shortcut here. Esc cancels. Insert is reserved.");
        }
        private void Finish()
        {
            if(!Capturing)return;Capturing=false;Text=shortcuts.Label(index+1);FlatAppearance.BorderColor=SpectraTheme.Border;if(capture!=null)capture(false);
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            if(!Capturing)return base.ProcessCmdKey(ref message,keyData);
            if(keyData==Keys.Escape) { feedback.Text=L.Text("Shortcut change cancelled.");Finish();return true; }
            Keys key=keyData&Keys.KeyCode;
            if(key==Keys.ControlKey||key==Keys.ShiftKey||key==Keys.Menu||key==Keys.LControlKey||key==Keys.RControlKey||key==Keys.LShiftKey||key==Keys.RShiftKey||key==Keys.LMenu||key==Keys.RMenu)return true;
            string error;
            if(shortcuts.TrySet(index,keyData,out error)) { feedback.ForeColor=SpectraTheme.Muted;feedback.Text=L.Format(L.Text("Saved: {0}"),shortcuts.Label(index+1));if(changed!=null)changed(); }
            else { feedback.ForeColor=Color.Orange;feedback.Text=error; }
            Finish();return true;
        }
        protected override void OnLostFocus(EventArgs e) { Finish();base.OnLostFocus(e); }
        protected override void Dispose(bool disposing) { if(disposing)Finish();base.Dispose(disposing); }
    }
    internal sealed class ShortcutEditor : FlowLayoutPanel
    {
        public ShortcutEditor(ShortcutOptions shortcuts,Action<bool> capture=null,Action changed=null)
        {
            Dock=DockStyle.Fill;AutoScroll=true;FlowDirection=FlowDirection.TopDown;WrapContents=false;Padding=new Padding(22,16,22,20);BackColor=SpectraTheme.Background;ForeColor=Color.White;Font=new Font("Segoe UI",11);
            Controls.Add(new Label { Text=L.Text("Keyboard shortcuts"),AutoSize=true,Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=SpectraTheme.Accent });
            Controls.Add(new Label { Text=L.Text("Click a shortcut, then press the new key here. Changes save automatically.\nInsert always collapses / expands the in-game panel."),Width=650,Height=62,ForeColor=SpectraTheme.Muted });
            Label feedback=new Label { Text=L.Text("Ctrl / Alt / Shift combinations are supported."),Width=680,Height=44,ForeColor=SpectraTheme.Muted };
            Controls.Add(feedback);
            for(int i=0;i<8;i++)
            {
                FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=56,WrapContents=false };
                row.Controls.Add(new Label { Text=L.Text(ShortcutOptions.Names[i]),RightToLeft=L.Direction,Width=390,Height=38,TextAlign=ContentAlignment.MiddleLeft });
                Button key=new ShortcutKeyButton(shortcuts,i,capture,changed,feedback);
                row.Controls.Add(key);Controls.Add(row);
            }
            SizeChanged+=delegate {
                int width=Math.Max(280,ClientSize.Width-Padding.Horizontal-26);
                foreach(Control child in Controls)
                {
                    child.Width=width;FlowLayoutPanel row=child as FlowLayoutPanel;if(row==null)continue;
                    int keyWidth=Math.Min(270,width/2);row.Controls[0].Width=Math.Max(100,width-keyWidth-20);row.Controls[1].Width=keyWidth;
                }
            };
        }
    }
}
