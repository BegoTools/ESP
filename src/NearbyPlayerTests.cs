using System;
using System.Drawing;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    internal static class NearbyPlayerTests
    {
        private static Actor Player(string address,float x)
        {
            return new Actor { Address=address,Kind="Character",Position=new Vec3(x,0,0),Health=100,MaxHealth=100,Height=180,Team=2,IsBot=false };
        }
        public static void Run(Action<bool,string> check)
        {
            RenderOptions options=new RenderOptions();
            Actor front=Player("front",10000),behind=Player("behind",-10000),bot=Player("bot",20000),ally=Player("ally",10000),training=Player("training",10000),vehicle=Player("vehicle",10000),dead=Player("dead",10000),far=Player("far",50001),invalid=Player("invalid",float.NaN);
            bot.IsBot=true;ally.Team=1;training.Kind="TrainingTarget";vehicle.Kind="Vehicle";dead.Health=0;behind.IsVisible=false;
            Frame frame=new Frame { CameraMode=true,Fov=90,LocalTeam=1,Actors=new[]{front,behind,bot,ally,training,vehicle,dead,far,invalid,front} };
            options.Allies=true;options.Alerts=false;options.Players.MaxVisible=1;options.LabelLimit=10;
            check(NearbyPlayers.Count(frame,options)==4,"nearby count includes players, bots and training targets, excludes team, noncharacters, dead, distant, invalid and duplicate actors, independent of draw limits");
            frame.CameraRotation=new Vec3(0,180,0);
            check(NearbyPlayers.Count(frame,options)==4,"turning the camera does not change the nearby count");
            options.ShowBots=false;
            check(NearbyPlayers.Count(frame,options)==4,"hidden bots still contribute to the nearby count");
            options.ShowPlayers=false;
            check(NearbyPlayers.Count(frame,options)==4,"hidden human players still contribute to the nearby count");
            behind.IsBot=null;front.Kind=null;front.IsBot=null;
            check(NearbyPlayers.Count(frame,options)==4,"unknown and legacy character records are counted alongside bots and training targets");
            options.ShowUnknownPlayers=false;
            check(NearbyPlayers.Count(frame,options)==4,"hidden unknown characters still contribute to the nearby count");
            options.Players.Enabled=false;options.Training.Enabled=false;
            check(NearbyPlayers.Count(frame,options)==4,"disabling player and training drawing categories does not suppress their count");
            options=new RenderOptions { MaxDistance=99 };
            check(NearbyPlayers.Count(frame,options)==0,"global distance can narrow the player category distance");
            options.MaxDistance=500;options.Players.MaxDistance=100;
            check(NearbyPlayers.Count(frame,options)==3,"characters on the player range boundary and training targets within their own range are included");
            options.Players.Enabled=false;
            check(NearbyPlayers.Count(frame,options)==3,"disabled player drawing category preserves nearby contacts");
            options.Training.MaxDistance=99;
            check(NearbyPlayers.Count(frame,options)==2,"training targets obey their own distance range");
            options.Training.MaxDistance=100;training.Team=frame.LocalTeam;
            check(NearbyPlayers.Count(frame,options)==3,"training targets are counted even when their team marker matches the local team");
            NativeSnapshot snapshot=new NativeSnapshot { LocalPawn="self",LocalTeam=301,CameraPosition=new Vec3(0,0,160),Origin=new Vec3(),Fov=90,Actors=new[]{
                new NativeActorData { Address="self",Kind="Character",Name="Local player",Health=100,MaxHealth=100,Team=301,Height=176 },
                new NativeActorData { Address="target26",Kind="TrainingTarget",Name="ModelTarget",Position=new Vec3(2600,0,88),Health=1000,MaxHealth=1000,Team=100000012,Height=176 },
                new NativeActorData { Address="target27",Kind="TrainingTarget",Name="ModelTarget",Position=new Vec3(2700,0,88),Health=1000,MaxHealth=1000,Team=100000012,Height=176 },
                new NativeActorData { Address="target29",Kind="TrainingTarget",Name="ModelTarget",Position=new Vec3(2900,0,88),Health=1000,MaxHealth=1000,Team=100000012,Height=176 },
                new NativeActorData { Address="target31",Kind="TrainingTarget",Name="ModelTarget",Position=new Vec3(3100,0,88),Health=1000,MaxHealth=1000,Team=100000012,Height=176 }
            }};
            check(NearbyPlayers.Count(snapshot.ToFrame(),new RenderOptions())==4,"native ModelTarget data at 26-31 metres yields four contacts after local player removal");
            options.Players.Enabled=true;frame.Actors=new Actor[0];
            check(NearbyPlayers.Count(frame,options)==0,"departed actors immediately clear the nearby count");
            frame.Actors=new[]{front};frame.UnitsPerMeter=0;
            check(NearbyPlayers.Count(frame,options)==0,"invalid world units cannot produce a nearby count");
            frame.UnitsPerMeter=100;frame.CapturedUtc=DateTime.UtcNow.AddSeconds(-3);
            check(NearbyPlayers.Count(frame,options)==0,"stale frames cannot retain a nearby count");

            // Empty scene isolates the badge from actor geometry and the optional status HUD.
            frame=new Frame();
            using(Bitmap shown=new Bitmap(1000,620))using(Bitmap hidden=new Bitmap(1000,620))
            using(Graphics a=Graphics.FromImage(shown))using(Graphics b=Graphics.FromImage(hidden))
            {
                a.Clear(Color.Black);b.Clear(Color.Black);
                Painter.Draw(a,shown.Size,frame,options,false,false);
                options.PlayerCountVisible=false;Painter.Draw(b,hidden.Size,frame,options,false,false);
                int changed=0;for(int y=12;y<55;y++)for(int x=365;x<636;x++)if(shown.GetPixel(x,y)!=hidden.GetPixel(x,y))changed++;
                check(changed>1000,"top center count draws even at zero with the status HUD hidden, and its toggle removes it");
                int yellow=0;for(int y=10;y<41;y++)for(int x=407;x<451;x++)if(shown.GetPixel(x,y).ToArgb()==Color.FromArgb(231,255,105).ToArgb())yellow++;
                check(yellow>10,"counter number uses a contrasting yellow green highlight even at zero");
                check(shown.GetPixel(380,20).ToArgb()==Color.Black.ToArgb()&&shown.GetPixel(500,46).ToArgb()==Color.Black.ToArgb(),"compact counter stays inside the new 196 by 30 pixel bounds");
                options.PlayerCountVisible=true;options.EspEnabled=false;a.Clear(Color.Black);Painter.Draw(a,shown.Size,frame,options,false,false);
                check(shown.GetPixel(500,20).ToArgb()==Color.Black.ToArgb(),"ESP off also hides the player count");
                options.EspEnabled=true;frame.CapturedUtc=DateTime.UtcNow.AddSeconds(-3);a.Clear(Color.Black);Painter.Draw(a,shown.Size,frame,options,false,false);
                check(shown.GetPixel(500,20).ToArgb()==Color.Black.ToArgb(),"stale data removes the badge instead of showing a misleading zero");
            }
            JavaScriptSerializer json=new JavaScriptSerializer();options.PlayerCountVisible=false;
            check(!json.Deserialize<RenderOptions>(json.Serialize(options)).PlayerCountVisible,"player count visibility survives saved settings");
            check(json.Deserialize<RenderOptions>("{\"Boxes\":false}").PlayerCountVisible,"existing settings enable the new player count by default");
        }
    }
}
