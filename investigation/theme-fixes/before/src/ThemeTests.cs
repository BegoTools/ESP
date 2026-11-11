using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class ThemeTests
    {
        private static object Field(object owner,string name) { return owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner); }
        private static void Call(object owner,string name,params object[] arguments) { owner.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,arguments); }
        public static string Run()
        {
            List<string> results=new List<string>();Action<bool,string> check=(condition,name)=>{ if(!condition)throw new Exception(name);results.Add("PASS "+name); };
            using(MainForm form=new MainForm(false,null,true))using(Form competitor=new Form())
            {
                RenderOptions options=(RenderOptions)Field(form,"options");ShortcutOptions shortcuts=options.Shortcuts;
                Keys chord=Keys.Control|Keys.Shift|Keys.Alt;
                shortcuts.Bindings[0]=(int)(chord|Keys.F10);shortcuts.Bindings[1]=(int)(chord|Keys.F11);shortcuts.Bindings[2]=(int)(chord|Keys.F12);
                try
                {
                    Call(form,"RebindShortcuts");HashSet<int> registered=(HashSet<int>)Field(form,"quickRegistered");
                    check(registered.Contains(1)&&registered.Contains(2)&&registered.Contains(3),"configured global shortcuts register with Windows");
                    check(!Native.RegisterHotKey(competitor.Handle,99,0x4007,0x79),"registered shortcut is owned by the application");
                    Call(form,"CaptureShortcuts",true);
                    check(Native.RegisterHotKey(competitor.Handle,99,0x4007,0x79),"shortcut capture releases the existing keyboard chord");
                    shortcuts.Bindings[0]=(int)(chord|Keys.F9);Call(form,"CaptureShortcuts",false);
                    check(registered.Contains(1)&&!Native.RegisterHotKey(competitor.Handle,98,0x4007,0x78),"rebind owns the new chord while the old chord is free for another window");
                    Call(form,"RebindShortcuts");check(registered.Contains(1),"repeated rebind releases its own previous registration");
                    Call(form,"ReleaseShortcuts");check(Native.RegisterHotKey(competitor.Handle,98,0x4007,0x78),"session cleanup releases the custom chord");
                    Call(form,"RebindShortcuts");Overlay overlay=(Overlay)Field(form,"overlay");
                    check(overlay.Quick.Unavailable.Contains(1)&&registered.Contains(2),"external shortcut conflict is reported without disabling the remaining actions");
                    SpectraDashboard dashboard=(SpectraDashboard)Field(form,"dashboard");dashboard.Size=new Size(1280,740);
                    bool original=options.EspEnabled;typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dashboard,new object[]{new MouseEventArgs(MouseButtons.Left,1,(int)(550*dashboard.Width/1280f),(int)(345*dashboard.Height/740f),0)});
                    check(options.EspEnabled!=original,"home switch updates the shared ESP option");
                    typeof(Control).GetMethod("OnMouseDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(dashboard,new object[]{new MouseEventArgs(MouseButtons.Left,1,(int)(955*dashboard.Width/1280f),(int)(462*dashboard.Height/740f),0)});
                    check(Math.Abs(options.Aim.Radius-610)<3,"home FOV slider updates the existing aim radius");
                    options.Aim.AimPoint="Legs";dashboard.RefreshValues();check(options.Aim.AimPoint=="Legs","home refresh keeps the selected target in sync");
                    check(SpectraTheme.Reference!=null,"reference artwork is embedded in the executable");
                    List<Button> navigation=(List<Button>)Field(form,"shellNavigation");check(navigation.FindAll(b=>b.BackColor!=SpectraTheme.Background).Count==1,"only the active sidebar destination is highlighted");
                    using(SpectraSlider slider=new SpectraSlider { Minimum=20,Maximum=1200,Value=90,Width=650,SmallChange=10 })
                    {
                        int notifications=0;slider.ValueChanged+=delegate { notifications++; };
                        slider.Value=9000;check(slider.Value==1200&&notifications==1,"themed settings slider clamps values and notifies its binding");
                        typeof(Control).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(slider,new object[]{new KeyEventArgs(Keys.Left)});
                        check(slider.Value==1190&&notifications==2,"themed settings slider supports keyboard adjustment");
                    }
                    using(SpectraChoice choice=new SpectraChoice())
                    {
                        choice.Items.AddRange(new object[]{"Head","Chest","Legs"});choice.SelectedIndex=0;
                        typeof(Control).GetMethod("OnKeyDown",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(choice,new object[]{new KeyEventArgs(Keys.Down)});
                        check(choice.SelectedIndex==1,"themed selector supports keyboard selection");
                    }
                }
                finally { Call(form,"ReleaseShortcuts");Native.UnregisterHotKey(competitor.Handle,98);Native.UnregisterHotKey(competitor.Handle,99); }
            }
            results.Add(results.Count+" Windows UI and hotkey checks passed. No game session started.");return String.Join(Environment.NewLine,results);
        }
    }
}
