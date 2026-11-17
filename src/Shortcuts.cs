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
    internal sealed class ShortcutKeyButton : SpectraButton
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
            Text=shortcuts.Label(index+1);
            AccessibleName=L.Text(ShortcutOptions.Names[index]);
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);Focus();if(Capturing)return;Capturing=true;if(capture!=null)capture(true);
            Text=L.Text("Press a key…");Highlighted=true;feedback.ForeColor=SpectraTheme.Accent;feedback.Text=L.Text("Press your new shortcut here. Esc cancels. Insert is reserved.");
        }
        private void Finish()
        {
            if(!Capturing)return;Capturing=false;Text=shortcuts.Label(index+1);Highlighted=false;if(capture!=null)capture(false);
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
    internal static class ShortcutEditor
    {
        internal static Control Create(ShortcutOptions shortcuts,Action<bool> capture,Action changed)
        {
            var page=new SpectraPage(L.Text("Keyboard shortcuts"),L.Text("Click a shortcut, then press the new key here. Changes save automatically.\nInsert always collapses / expands the in-game panel."));
            SpectraCard session=page.Group(L.Text("Session and panel"));
            SpectraCard controls=page.Group(L.Text("In-game controls"),1);
            Label feedback=SpectraFields.Note(L.Text("Ctrl / Alt / Shift combinations are supported."));
            page.Group(L.Text("Shortcut feedback")).Add(feedback);
            for(int i=0;i<8;i++)
            {
                SpectraCard group=i<3||i==7?session:controls;
                group.Add(SpectraFields.Pair(SpectraFields.Caption(L.Text(ShortcutOptions.Names[i])),
                    new ShortcutKeyButton(shortcuts,i,capture,changed,feedback),142,45));
            }
            return page;
        }
    }
}
