using System;
using System.Drawing;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class QuickTests
    {
        public static void Run(Action<bool,string> check)
        {
            RenderOptions options=new RenderOptions();options.Aim.Enabled=true;
            QuickActions.Apply(options,3);check(!options.Aim.Enabled&&options.EspEnabled,"F3 toggles aim independently of ESP");QuickActions.Apply(options,3);
            options.Aim.Activation="Firing";QuickActions.Apply(options,4);check(options.Aim.Activation=="Always","F4 advances to the next activation mode");
            for(int i=0;i<4;i++)QuickActions.Apply(options,4);check(options.Aim.Activation=="Firing","F4 cycles all modes and returns to the starting mode");
            options.Aim.AimPoint="Chest";QuickActions.Apply(options,5);check(options.Aim.AimPoint=="Legs","F5 cycles the selected body region");QuickActions.Apply(options,5);QuickActions.Apply(options,5);check(options.Aim.AimPoint=="Chest","F5 body-region cycle is reversible");
            QuickActions.Apply(options,6);check(!options.EspEnabled&&options.Aim.Enabled&&options.Players.Enabled,"F6 hides drawing without disabling aim or changing category filters");
            QuickActions.Apply(options,6);check(options.EspEnabled,"F6 restores ESP drawing");
            options.Aim.Strength=99;QuickActions.Adjust(options,0,1);check(options.Aim.Strength==100,"quick strength clamps at 100 percent");options.Aim.Strength=1;QuickActions.Adjust(options,0,-1);check(options.Aim.Strength==0,"quick strength clamps at zero");
            options.Aim.Radius=1199;QuickActions.Adjust(options,1,1);check(options.Aim.Radius==1200,"quick radius clamps at its maximum");options.Aim.Radius=21;QuickActions.Adjust(options,1,-1);check(options.Aim.Radius==20,"quick radius clamps at its minimum");
            options.Aim.InputScale=4.99f;QuickActions.Adjust(options,2,1);check(options.Aim.InputScale==5,"quick input scale clamps at its maximum");options.Aim.InputScale=.11f;QuickActions.Adjust(options,2,-1);check(Math.Abs(options.Aim.InputScale-.1f)<.001,"quick input scale clamps at its minimum");
            options.EspEnabled=false;options.QuickHudVisible=false;
            RenderOptions restored=new JavaScriptSerializer().Deserialize<RenderOptions>(new JavaScriptSerializer().Serialize(options));restored.EnsureDefaults();
            check(!restored.EspEnabled&&!restored.QuickHudVisible&&restored.Aim.Enabled,"quick drawing preferences persist independently of aim");
            RenderOptions legacy=new JavaScriptSerializer().Deserialize<RenderOptions>("{\"Names\":false}");legacy.EnsureDefaults();check(legacy.EspEnabled&&legacy.QuickHudVisible,"legacy settings enable the shortcut HUD and preserve ESP by default");
            check(legacy.Shortcuts.Label(4)=="F3"&&legacy.Shortcuts.Key(8)==(Keys.Control|Keys.F7),"legacy settings retain existing shortcut defaults");
            string error;
            check(options.Shortcuts.TrySet(3,Keys.Control|Keys.K,out error),"input toggle accepts a custom keyboard chord");
            check(options.Shortcuts.Modifiers(4)==0x4002u&&options.Shortcuts.Key(4)==(Keys.Control|Keys.K),"custom chord produces the Windows key and modifier binding");
            check(!options.Shortcuts.TrySet(6,Keys.Control|Keys.K,out error)&&options.Shortcuts.Key(7)==Keys.F6,"duplicate shortcut is rejected without replacing the previous binding");
            check(!options.Shortcuts.TrySet(0,Keys.Insert,out error)&&!options.Shortcuts.TrySet(0,Keys.Control|Keys.Insert,out error),"Insert stays reserved for panel collapse with or without modifiers");
            check(!options.Shortcuts.TrySet(0,Keys.Up,out error)&&!options.Shortcuts.TrySet(0,Keys.ControlKey,out error),"panel navigation and standalone modifier keys cannot become action shortcuts");
            RenderOptions custom=new JavaScriptSerializer().Deserialize<RenderOptions>(new JavaScriptSerializer().Serialize(options));custom.EnsureDefaults();
            check(custom.Shortcuts.Key(4)==(Keys.Control|Keys.K),"custom shortcuts survive saving and reloading settings");
            custom.Shortcuts.Bindings=new[]{1,2};custom.EnsureDefaults();check(custom.Shortcuts.Key(4)==Keys.F3,"malformed shortcut settings recover existing defaults");
            TestPanel(check,legacy);
            using(Bitmap canvas=new Bitmap(1000,600))using(Graphics graphics=Graphics.FromImage(canvas))
            {
                Frame frame=new Frame { CameraMode=true,Fov=5,Actors=new[]{new Actor { Kind="Character",Position=new Vec3(100,0,0),Height=180,Health=100,MaxHealth=100,Team=2 }} };
                Painter.Draw(graphics,canvas.Size,frame,new RenderOptions { EspEnabled=false },true);
                check(canvas.GetPixel(1,200).G<100,"ESP master switch actually suppresses actor rendering");
                QuickHud.Draw(graphics,canvas.Size,legacy,new QuickHudState { Section=QuickHudSection.Adjustments,SelectedRow=2 },"ready");
                check(canvas.GetPixel(30,82).ToArgb()!=Color.FromArgb(14,22,33).ToArgb(),"expanded quick HUD draws a readable background over the scene");
            }
        }

        private static void TestPanel(Action<bool,string> check,RenderOptions options)
        {
            Size normal=new Size(1920,1080),small=new Size(180,110);
            Point initial=new Point(options.QuickHudX,options.QuickHudY);
            QuickHudState state=new QuickHudState();
            QuickHudLayout medium=new QuickHudLayout(normal,state);
            check(initial==new Point(12,12)&&medium.Bounds(initial).Location==initial,"legacy settings place the panel in its original corner");
            state.ToggleCollapse(options,false);
            QuickHudLayout strip=new QuickHudLayout(normal,state);
            check(strip.Bounds(initial).Width<medium.Bounds(initial).Width&&strip.Bounds(initial).Location==initial,"collapsed panel stays at the selected position");
            state.Hidden=true;
            check(!state.IsVisible(options,false),"hide removes the collapsed panel");
            state.ToggleCollapse(options,false);
            check(!state.Collapsed&&state.IsVisible(options,false),"Insert restores a hidden panel");

            state.ToggleSection(QuickHudSection.Adjustments);
            QuickHudLayout expanded=new QuickHudLayout(small,state);
            Rectangle clamped=expanded.Bounds(new Point(800,500));
            check(new Rectangle(Point.Empty,small).Contains(clamped)&&expanded.Scale<1,"panel scales and clamps inside a small game window");
            Rectangle hit=expanded.Area(QuickHudAction.Radius);
            Point scaledCenter=new Point((int)((hit.X+hit.Width/2)*expanded.Scale),(int)((hit.Y+hit.Height/2)*expanded.Scale));
            check(expanded.HitTest(scaledCenter)==QuickHudAction.Radius&&expanded.HitTest(new Point(-1,0))==QuickHudAction.None,"scaled mouse targets match the drawn controls");
            QuickActions.SetAdjustment(options,0,-1);
            QuickActions.SetAdjustment(options,1,2);
            QuickActions.SetAdjustment(options,2,.5f);
            check(options.Aim.Strength==0&&options.Aim.Radius==1200&&Math.Abs(options.Aim.InputScale-2.55f)<.001,"mouse adjustments clamp values with the existing slider ranges");
            state.ToggleSection(QuickHudSection.Display);
            check(!state.Open&&state.IsVisible(options,true),"display controls are accessible while paused and release the adjustment arrows");
            options.QuickHudX=780;
            options.QuickHudY=420;
            RenderOptions restored=new JavaScriptSerializer().Deserialize<RenderOptions>(new JavaScriptSerializer().Serialize(options));
            check(restored.QuickHudX==780&&restored.QuickHudY==420,"dragged panel position survives settings reload");
            options.QuickHudX=initial.X;
            options.QuickHudY=initial.Y;
        }
    }
}
