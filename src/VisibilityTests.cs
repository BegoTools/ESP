using System;
using System.Drawing;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    internal static class VisibilityTests
    {
        private static Actor Enemy(string address,float lateral,bool? visible)
        {
            return new Actor { Address=address,Kind="Character",Team=2,Position=new Vec3(10000,lateral,-117),Height=180,Health=100,MaxHealth=100,IsVisible=visible,VisibilitySource="Fixture visibility" };
        }
        private static Frame Scene(params Actor[] actors)
        {
            return new Frame { CameraMode=true,Fov=90,LocalTeam=1,Actors=actors };
        }
        public static void Run(Action<bool,string> check)
        {
            Size viewport=new Size(1920,1080);
            JavaScriptSerializer json=new JavaScriptSerializer();
            RenderOptions migrated=json.Deserialize<RenderOptions>("{\"Aim\":{\"Enabled\":true,\"VisibleOnly\":true,\"Activation\":\"Always\",\"AimPoint\":\"Head\"}}");
            migrated.EnsureDefaults();
            AimOptions aim=migrated.Aim;
            check(aim.AimPoint=="Head"&&aim.Activation=="Always"&&!json.Serialize(migrated).Contains("\"VisibleOnly\""),"the retired option is dropped when resaving an older profile and other aim settings persist");

            Actor hidden=Enemy("hidden",100,false),partial=Enemy("partially-exposed",600,true),unknown=Enemy("unknown",50,null);
            check(AimEngine.Select(Scene(hidden,partial,unknown),viewport,aim,null).Address==unknown.Address,"an older profile uses normal aim with unknown visibility");
            check(AimEngine.Select(Scene(hidden),viewport,aim,null).Address==hidden.Address,"normal aim accepts a valid target with occluded visibility");
            Actor training=Enemy("training",200,true);training.Kind="TrainingTarget";training.Team=0;

            RenderOptions options=new RenderOptions();options.EnsureDefaults();
            options.Visibility.VisibleColorArgb=Color.FromArgb(18,207,131).ToArgb();
            options.Visibility.OccludedColorArgb=Color.FromArgb(83,121,229).ToArgb();
            options.Visibility.UnknownColorArgb=Color.FromArgb(161,153,145).ToArgb();
            int normalColor=options.Players.ColorArgb;
            check(Painter.ActorColor(hidden,options,false).ToArgb()==normalColor,"disabled visibility colors preserve the existing player category color");
            options.Visibility.Enabled=true;
            check(Painter.ActorColor(partial,options,false).ToArgb()==options.Visibility.VisibleColorArgb,"partial exposure uses the customizable visible color");
            check(Painter.ActorColor(hidden,options,false).ToArgb()==options.Visibility.OccludedColorArgb,"occluded characters use the customizable occluded color");
            check(Painter.ActorColor(unknown,options,false).ToArgb()==options.Visibility.UnknownColorArgb,"unknown visibility has a neutral state instead of pretending occlusion");
            Actor vehicle=Enemy("vehicle",100,false);vehicle.Kind="Vehicle";
            check(Painter.ActorColor(vehicle,options,false).ToArgb()==options.Vehicles.ColorArgb,"visibility colors leave the vehicle category unchanged");
            training.IsVisible=true;
            check(Painter.ActorColor(training,options,false).ToArgb()==options.Visibility.VisibleColorArgb,"training target rendering uses the same visible-state palette");
            options.Boxes=true;options.Lines=true;options.Names=options.Distance=options.Health=options.Alerts=false;
            Actor rendered=Enemy("rendered",0,false);rendered.Position.X=1000;
            using(Bitmap bitmap=new Bitmap(800,450))using(Graphics graphics=Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Black);Painter.Draw(graphics,bitmap.Size,Scene(rendered),options,false,false);
                int matches=0;for(int y=185;y<285;y++)for(int x=365;x<435;x++)if(bitmap.GetPixel(x,y).ToArgb()==options.Visibility.OccludedColorArgb)matches++;
                check(matches>70,"actual ESP box pixels use the selected occluded color");
                graphics.Clear(Color.Black);rendered.IsVisible=true;Painter.Draw(graphics,bitmap.Size,Scene(rendered),options,false,false);
                matches=0;for(int y=185;y<285;y++)for(int x=365;x<435;x++)if(bitmap.GetPixel(x,y).ToArgb()==options.Visibility.VisibleColorArgb)matches++;
                check(matches>70,"actual ESP box pixels return to the visible color on partial exposure");
            }

            FrameBuffer buffer=new FrameBuffer();DateTime at=DateTime.UtcNow;
            Actor previous=Enemy("same",0,true),current=Enemy("same",100,false);current.VisibilitySource="Current fixture";
            Frame first=Scene(previous),last=Scene(current);first.CapturedUtc=at;last.CapturedUtc=at.AddMilliseconds(20);buffer.Push(first);buffer.Push(last);
            Frame sampled=buffer.SampleAt(at.AddMilliseconds(10));
            check(sampled.Actors[0].IsVisible==false&&sampled.Actors[0].VisibilitySource=="Current fixture","display interpolation carries the newer discrete visibility state and source");
            current.IsVisible=null;
            check(buffer.SampleAt(at.AddMilliseconds(10)).Actors[0].IsVisible==null,"display interpolation does not invent a visible state when current data is unknown");
            RenderOptions restored=json.Deserialize<RenderOptions>(json.Serialize(options));restored.EnsureDefaults();
            check(restored.Visibility.Enabled&&restored.Visibility.VisibleColorArgb==options.Visibility.VisibleColorArgb&&restored.Visibility.OccludedColorArgb==options.Visibility.OccludedColorArgb&&restored.Visibility.UnknownColorArgb==options.Visibility.UnknownColorArgb,"visibility colors and all three customized values survive settings persistence");
            RenderOptions legacy=json.Deserialize<RenderOptions>("{\"Boxes\":false}");legacy.EnsureDefaults();
            check(!legacy.Visibility.Enabled,"legacy settings preserve the original color policy");
            legacy.Visibility=null;legacy.EnsureDefaults();
            check(legacy.Visibility!=null&&!legacy.Visibility.Enabled,"missing visibility settings recover without enabling an unverified data dependency");
            Actor actorCopy=json.Deserialize<Actor>(json.Serialize(partial));
            check(actorCopy.IsVisible==true&&actorCopy.VisibilitySource==partial.VisibilitySource,"actor JSON preserves visibility and its reported source");
            NativeActorData native=new NativeActorData { Address="enemy",Kind="Character",Position=new Vec3(1000,0,90),Height=180,Health=100,MaxHealth=100,IsVisible=true,VisibilitySource="Verified fixture" };
            Actor converted=NativeSnapshot.ConvertActors(new[]{native},"local")[0];
            check(converted.IsVisible==true&&converted.VisibilitySource=="Verified fixture","native transport carries explicit visibility state and source into the aim/render frame");
            native.IsVisible=null;native.VisibilitySource=null;converted=NativeSnapshot.ConvertActors(new[]{native},"local")[0];
            check(converted.IsVisible==null&&converted.VisibilitySource==null,"an older native payload without visibility stays unknown instead of becoming occluded");
        }
    }
}
