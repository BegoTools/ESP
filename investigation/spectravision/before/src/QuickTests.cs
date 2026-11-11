using System;
using System.Drawing;
using System.Web.Script.Serialization;

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
            using(Bitmap canvas=new Bitmap(1000,600))using(Graphics graphics=Graphics.FromImage(canvas))
            {
                Frame frame=new Frame { CameraMode=true,Fov=5,Actors=new[]{new Actor { Kind="Character",Position=new Vec3(100,0,0),Height=180,Health=100,MaxHealth=100,Team=2 }} };
                Painter.Draw(graphics,canvas.Size,frame,new RenderOptions { EspEnabled=false },true);
                check(canvas.GetPixel(1,200).G<100,"ESP master switch actually suppresses actor rendering");
                QuickHud.Draw(graphics,canvas.Size,legacy,new QuickHudState { Open=true,SelectedRow=2 },"ready");
                check(canvas.GetPixel(17,17).R<40,"expanded quick HUD renders a solid readable panel");
            }
        }
    }
}
