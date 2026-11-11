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
        private static IEnumerable<Control> Children(Control parent) { foreach(Control child in parent.Controls) { yield return child;foreach(Control item in Children(child))yield return item; } }


        private static void CheckLanguagePersistence(Action<bool,string> check)
        {
            string file=Settings.FilePath;
            byte[] previous=System.IO.File.Exists(file)?System.IO.File.ReadAllBytes(file):null;
            string previousLanguage=L.Language;
            try
            {
                using(MainForm form=new MainForm())
                {
                    Call(form,"ChangeLanguage",3);
                    check(Settings.Load().Language=="fr","changing language through the normal settings action saves to disk");
                }
                using(MainForm reopened=new MainForm(false,null,true))
                {
                    var navigation=(List<Button>)Field(reopened,"shellNavigation");
                    check(L.Language=="fr"&&navigation[0].Text.Contains("Accueil"),"a fresh window restores the saved language");
                }
            }
            finally
            {
                if(previous!=null)UserData.Replace(file,previous);
                else if(System.IO.File.Exists(file))System.IO.File.Delete(file);
                L.UseLanguage(previousLanguage);
            }
        }

        private static void CheckDropdown(MainForm form, Action<bool,string> check)
        {
            form.StartPosition=FormStartPosition.Manual;
            form.Location=new Point(-20000,-20000);
            form.ShowInTaskbar=false;
            form.Show();
            Application.DoEvents();
            var options=(RenderOptions)Field(form,"options");
            for(int page=0;page<=2;page+=2)
            {
                Call(form,"NavigateShell",page);
                foreach(Control child in Children(form))
                {
                    SpectraChoice choice=child as SpectraChoice;
                    if(choice==null||choice.Items.Count<2)continue;
                    int notifications=0;
                    choice.SelectedIndexChanged+=delegate { notifications++; };
                    for(int attempt=0;attempt<4;attempt++)
                    {
                        int index=(choice.SelectedIndex+1)%choice.Items.Count;
                        Call(choice,"OnClick",EventArgs.Empty);
                        var menu=(ContextMenuStrip)Field(choice,"menu");
                        check(menu.Visible,"dropdown opens on page "+page);
                        ((ToolStripMenuItem)menu.Items[index]).PerformClick();
                        menu.Close(ToolStripDropDownCloseReason.ItemClicked);
                        Application.DoEvents();
                        check(!menu.IsDisposed&&choice.SelectedIndex==index,"dropdown selection finishes with a reusable native menu on page "+page);
                        Call(choice,"OnClick",EventArgs.Empty);
                        menu.Close(ToolStripDropDownCloseReason.AppClicked);
                        Application.DoEvents();
                        check(!menu.IsDisposed,"outside-click closure keeps the dropdown alive on page "+page);
                        Call(choice,"OnClick",EventArgs.Empty);
                        menu.Close(ToolStripDropDownCloseReason.Keyboard);
                        Application.DoEvents();
                    }
                    check(notifications==4,"each dropdown selection changes its setting once");
                }
            }
            check(options.Aim.AimPoint=="Head"||options.Aim.AimPoint=="Chest"||options.Aim.AimPoint=="Legs","menu choices retain stored aim identifiers");
            form.Hide();
        }

        private static void CheckLanguages(MainForm form, Action<bool,string> check)
        {
            string previous=L.Language;
            var options=(RenderOptions)Field(form,"options");
            options.Aim.AimPoint="Head";
            options.Aim.Activation="ScopedAndFiring";
            int strength=options.Aim.Strength;
            var navigation=(List<Button>)Field(form,"shellNavigation");
            var language=(SpectraChoice)Field(form,"languageChoice");
            try
            {
                Call(form,"SetStatus","{0} connected / run game inspection",new object[]{"GameLoop"});
                Call(form,"WriteLogText","Display and reader stopped.",new object[0]);
                string[] espNames={"ESP","مساعدات استكشاف","Erkundungshilfen","Aides à l’exploration"};
                for(int index=0;index<4;index++)
                {
                    language.SelectedIndex=index;
                    check(L.Index==index&&((Settings)Field(form,"settings")).Language==L.CodeAt(index),"language selector updates the shared preference for "+L.Language);
                    check(form.Text=="RAM HAX"&&navigation[0].Text.Contains(L.Text("Home")),"window branding and navigation use "+L.Language);
                    check(L.Text("ESP")==espNames[index]&&navigation[1].Text.Contains(espNames[index]),"the exploration aids name appears in navigation in "+L.Language);
                    check(((Label)Field(form,"status")).Text==L.Format("{0} connected / run game inspection","GameLoop"),"existing status retranslates into "+L.Language);
                    check(((TextBox)Field(form,"log")).Text.Contains(L.Text("Display and reader stopped.")),"existing application log retranslates into "+L.Language);
                    Call(form,"NavigateShell",2);
                    Panel shell=(Panel)Field(form,"shellContent");
                    bool pointFound=false;
                    int aimChoices=0;
                    foreach(Control child in Children(shell))
                    {
                        SpectraChoice choice=child as SpectraChoice;
                        if(choice==null)continue;
                        aimChoices++;
                        if(choice.Items.Contains(L.Text("الرأس (تقديري)")))
                            pointFound=choice.SelectedIndex==0&&choice.RightToLeft==L.Direction;
                    }
                    check(pointFound,"aim labels and text direction match "+L.Language);
                    check(aimChoices==3,"the Aim Assist page exposes only point, activation, and hold-key selectors in "+L.Language);
                    check(options.Aim.AimPoint=="Head"&&options.Aim.Activation=="ScopedAndFiring"&&options.Aim.Strength==strength,"switching language preserves settings for "+L.Language);
                    foreach(int page in new[]{1,3,4,5,6,0})
                    {
                        Call(form,"NavigateShell",page);
                        form.PerformLayout();
                        check(form.OwnedForms.Length==0,"localized page "+page+" opens in "+L.Language);
                    }
                    using(OptionsForm factory=new OptionsForm(options,delegate {}))
                    {
                        foreach(int page in new[]{0,5,6,7})using(Control content=factory.CreateEmbeddedPage(page))
                        {
                            int marks=0;
                            foreach(Control child in Children(content))
                                if(child.Text.Contains(L.Format("{0} (under development)","").Trim()))marks++;
                            check(marks==(page==0?3:1),"development labels remain attached to the pictured options on page "+page+" in "+L.Language);
                        }
                    }
                    var saved=new Settings { Language=L.Language,Options=options };
                    var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
                    check(serializer.Deserialize<Settings>(serializer.Serialize(saved)).Language==L.Language,"language preference round-trips for "+L.Language);
                }
                check(new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{}").Language=="ar","older settings default to Arabic");
                L.UseLanguage("unknown");
                check(L.Language=="ar","unsupported saved language falls back to Arabic");
            }
            finally
            {
                L.UseLanguage(previous);
                ((Settings)Field(form,"settings")).Language=previous;
                language.SelectedIndex=L.Index;
                Call(form,"RefreshLanguage");
            }
        }

        public static string RunInMessageLoop()
        {
            string result=null;
            Exception failure=null;
            using(var context=new ApplicationContext())
            using(var runner=new System.Windows.Forms.Timer { Interval=1 })
            {
                runner.Tick+=delegate
                {
                    runner.Stop();
                    try { result=Run(); }
                    catch(Exception ex) { failure=ex; }
                    finally { context.ExitThread(); }
                };
                runner.Start();
                Application.Run(context);
            }
            if(failure!=null)throw failure;
            return result;
        }

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
                    Panel shell=(Panel)Field(form,"shellContent");
                    for(int page=1;page<=5;page++)
                    {
                        Call(form,"NavigateShell",page);form.PerformLayout();shell.PerformLayout();bool oldNavigation=false;
                        foreach(Control child in Children(shell))if(child is Form||child is ListBox||child is TabControl)oldNavigation=true;
                        check(!oldNavigation&&form.OwnedForms.Length==0,"shell page "+page+" uses direct content without an old sidebar or popup");
                    }
                    ShortcutKeyButton binding=null;foreach(Control child in Children(shell))if(child is ShortcutKeyButton&&child.Text==shortcuts.Label(4))binding=(ShortcutKeyButton)child;
                    check(binding!=null,"Shortcuts exposes the input binding on the main screen");
                    Call(binding,"OnClick",EventArgs.Empty);check(binding.Capturing&&registered.Count==0&&form.OwnedForms.Length==0,"inline capture suspends shortcuts without opening a window");
                    object[] command={Message.Create(binding.Handle,0x100,IntPtr.Zero,IntPtr.Zero),Keys.Control|Keys.K};
                    binding.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(binding,command);
                    check(!binding.Capturing&&shortcuts.Key(4)==(Keys.Control|Keys.K)&&binding.Text=="Ctrl+K","inline key capture updates the shared binding and exits capture");
                    Call(binding,"OnClick",EventArgs.Empty);command[1]=Keys.Escape;binding.GetType().GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(binding,command);
                    check(!binding.Capturing&&shortcuts.Key(4)==(Keys.Control|Keys.K),"Escape cancels inline capture without changing the shortcut");
                    Call(binding,"OnClick",EventArgs.Empty);Call(form,"NavigateShell",2);
                    check(registered.Contains(2)&&!(bool)Field(form,"shortcutsSuspended"),"leaving Shortcuts during capture restores global bindings");
                    using(SpectraSwitch toggle=new SpectraSwitch { Text="A clean row",Size=new Size(400,36) })
                    using(SpectraSlider slider=new SpectraSlider { Size=new Size(400,42) })
                    foreach(Control control in new Control[]{toggle,slider})using(Bitmap bitmap=new Bitmap(control.Width,control.Height))using(Graphics graphics=Graphics.FromImage(bitmap))
                    {
                        graphics.Clear(Color.Magenta);Call(control,"OnPaint",new PaintEventArgs(graphics,new Rectangle(Point.Empty,control.Size)));
                        check(bitmap.GetPixel(0,0).ToArgb()==SpectraTheme.Background.ToArgb(),control.GetType().Name+" clears stale background pixels on every paint");
                    }
                    check(AdministratorLaunch.NeedsElevation(0x2000)&&AdministratorLaunch.NeedsElevation(-1)&&!AdministratorLaunch.NeedsElevation(0x3000),"Home START requires elevation unless the current process already has high integrity");
                    System.Diagnostics.ProcessStartInfo launch=AdministratorLaunch.StartInfo("tool.exe",EmulatorKind.GameLoop,"C:\\test");
                    check(launch.Verb=="runas"&&launch.UseShellExecute&&launch.Arguments.Contains("--live")&&launch.Arguments.Contains("--restart-elevated")&&launch.Arguments.Contains("GameLoop"),"administrator restart requests Windows elevation and resumes the selected emulator session");
                }
                finally { Call(form,"ReleaseShortcuts");Native.UnregisterHotKey(competitor.Handle,98);Native.UnregisterHotKey(competitor.Handle,99); }
            }
            CheckLanguagePersistence(check);
            using(MainForm form=new MainForm(false,null,true))
            {
                CheckDropdown(form,check);
                CheckLanguages(form,check);
                form.Close();
                DateTime deadline=DateTime.UtcNow.AddSeconds(5);
                while(!form.IsDisposed&&DateTime.UtcNow<deadline) { Application.DoEvents();System.Threading.Thread.Sleep(10); }
                check(form.IsDisposed,"UI regression form shuts down cleanly");
            }
            results.Add(results.Count+" Windows UI and hotkey checks passed. No game session started.");return String.Join(Environment.NewLine,results);
        }
    }
}
