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
            if(index<0||index>=8||!Allowed(value)) { error="Choose another key. Insert is reserved for collapsing the overlay; arrows adjust it.";return false; }
            for(int i=0;i<8;i++)if(i!=index&&Bindings[i]==(int)value) { error="Already assigned to: "+Names[i];return false; }
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
    internal sealed class KeyCaptureDialog : Form
    {
        public Keys Captured;
        public KeyCaptureDialog()
        {
            Text="Change shortcut";ClientSize=new Size(470,130);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;KeyPreview=true;BackColor=SpectraTheme.Background;ForeColor=Color.White;
            Controls.Add(new Label { Dock=DockStyle.Fill,Text="Press a key or Ctrl / Alt / Shift + key\nEsc cancels · Insert is reserved",TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",12) });
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
        {
            if(keyData==Keys.Escape) { DialogResult=DialogResult.Cancel;return true; }
            if(ShortcutOptions.Allowed(keyData)) { Captured=keyData;DialogResult=DialogResult.OK;return true; }
            return true;
        }
    }
    internal sealed class ShortcutEditor : FlowLayoutPanel
    {
        public ShortcutEditor(ShortcutOptions shortcuts,Action<bool> capture=null)
        {
            Dock=DockStyle.Fill;AutoScroll=true;FlowDirection=FlowDirection.TopDown;WrapContents=false;Padding=new Padding(20);BackColor=SpectraTheme.Background;ForeColor=Color.White;
            Controls.Add(new Label { Text="Keyboard shortcuts",AutoSize=true,Font=new Font("Segoe UI",23,FontStyle.Bold),ForeColor=SpectraTheme.Accent });
            Controls.Add(new Label { Text="Click a key to change it. Changes save when you close settings.\nInsert always collapses / expands the in-game panel.",Width=650,Height=62,ForeColor=SpectraTheme.Muted });
            for(int i=0;i<8;i++)
            {
                int index=i;FlowLayoutPanel row=new FlowLayoutPanel { Width=680,Height=50,WrapContents=false };
                row.Controls.Add(new Label { Text=ShortcutOptions.Names[i],Width=390,Height=38,TextAlign=ContentAlignment.MiddleLeft });
                Button key=new Button { Text=shortcuts.Label(i+1),Width=230,Height=38,BackColor=SpectraTheme.Surface,ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
                key.Click+=delegate {
                    if(capture!=null)capture(true);
                    try { using(KeyCaptureDialog dialog=new KeyCaptureDialog())if(dialog.ShowDialog(FindForm())==DialogResult.OK) { string error;if(shortcuts.TrySet(index,dialog.Captured,out error))key.Text=shortcuts.Label(index+1);else MessageBox.Show(FindForm(),error,"Shortcut unavailable",MessageBoxButtons.OK,MessageBoxIcon.Information); } }
                    finally { if(capture!=null)capture(false); }
                };
                row.Controls.Add(key);Controls.Add(row);
            }
        }
    }
}
