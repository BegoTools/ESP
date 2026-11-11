using System;
using System.Drawing;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    internal static class KnockdownTests
    {
        private static NativeActorData Enemy()
        {
            return new NativeActorData { Address="enemy",Name="Opponent",Kind="Character",Position=new Vec3(1000,0,-29),Height=176,Health=100,MaxHealth=100,Team=2,IsBot=false,IsDead=false,NearDeathBreath=100 };
        }
        private static Frame Transport(NativeActorData enemy)
        {
            NativeSnapshot snapshot=new NativeSnapshot { LocalPawn="local",LocalTeam=1,Fov=90,Actors=new[]{
                new NativeActorData { Address="local",Kind="Character",Health=100,MaxHealth=100,Team=1 },enemy
            }};
            return NativeSnapshot.Parse(new JavaScriptSerializer().Serialize(snapshot)).ToFrame();
        }
        private static int Pixels(Bitmap bitmap,Rectangle region,Color color)
        {
            int count=0;
            for(int y=region.Top;y<region.Bottom;y++)for(int x=region.Left;x<region.Right;x++)if(bitmap.GetPixel(x,y).ToArgb()==color.ToArgb())count++;
            return count;
        }
        public static void Run(Action<bool,string> check)
        {
            NativeActorData enemy=Enemy();RenderOptions options=new RenderOptions();
            Frame alive=Transport(enemy);
            check(alive.Actors.Length==1&&!alive.Actors[0].IsKnocked&&NearbyPlayers.Count(alive,options)==1,"native alive opponent survives transport while local pawn stays excluded");
            enemy.Health=0;enemy.NearDeathBreath=73.5f;
            Frame knocked=Transport(enemy);
            check(knocked.Actors.Length==1&&knocked.Actors[0].IsKnocked&&knocked.Actors[0].IsDead==false&&knocked.Actors[0].NearDeathBreath==73.5f,"zero-health opponent survives native JSON conversion with confirmed knockdown and breath intact");
            check(NearbyPlayers.Count(knocked,options)==1,"nearby count retains an opponent throughout knockdown");
            AimOptions aim=new AimOptions { Enabled=true,Activation="Always",Radius=500,MaxDistance=150 };
            check(AimEngine.Select(knocked,new Size(800,450),aim,null)==null,"showing a knocked opponent does not make the aimbot target zero-health players");
            enemy.Health=25;
            Frame revived=Transport(enemy);
            check(revived.Actors.Length==1&&!revived.Actors[0].IsKnocked&&NearbyPlayers.Count(revived,options)==1,"revival removes knockdown state without removing the opponent from the nearby count");
            check(AimEngine.Select(revived,new Size(800,450),aim,null)!=null,"revived opponent returns to the existing eligible aim selection");
            enemy.Health=0;enemy.IsDead=true;
            Frame finished=Transport(enemy);
            check(finished.Actors.Length==0&&NearbyPlayers.Count(finished,options)==0,"finishing a knocked opponent removes it even if an earlier positive breath value remains");
            enemy=Enemy();enemy.IsDead=true;
            check(Transport(enemy).Actors.Length==0,"authoritative death excludes a corpse even during a stale positive-health frame");
            enemy.Health=0;enemy.IsDead=false;enemy.NearDeathBreath=0;
            check(Transport(enemy).Actors.Length==0,"exhausted knockdown breath removes the opponent instead of keeping a phantom marker");
            enemy.NearDeathBreath=70;enemy.IsDead=null;
            check(Transport(enemy).Actors.Length==0,"unknown death state cannot turn a zero-health actor into a confirmed knockdown");
            enemy.IsDead=false;enemy.NearDeathBreath=null;
            check(Transport(enemy).Actors.Length==0,"missing knockdown breath preserves zero-health rejection");
            check(!CharacterLife.Retain(0,100,false,float.NaN)&&!CharacterLife.Retain(0,100,false,float.PositiveInfinity)&&!CharacterLife.Retain(-1,100,false,60)&&!CharacterLife.Retain(0,0,false,60),"invalid breath, negative health and missing maximum health cannot retain phantom knocked actors");
            string legacy="{\"Fov\":90,\"LocalTeam\":1,\"LocalPawn\":\"local\",\"Actors\":[{\"Address\":\"old-alive\",\"Kind\":\"Character\",\"Health\":100,\"MaxHealth\":100,\"Team\":2},{\"Address\":\"old-zero\",\"Kind\":\"Character\",\"Health\":0,\"MaxHealth\":100,\"Team\":2}]}";
            Frame old=NativeSnapshot.Parse(legacy).ToFrame();
            check(old.Actors.Length==1&&old.Actors[0].Address=="old-alive"&&old.Actors[0].IsDead==null&&old.Actors[0].NearDeathBreath==null&&!old.Actors[0].IsKnocked,"legacy native payloads retain living players without inventing knockdown for missing life fields");
            enemy=Enemy();enemy.Health=0;enemy.NearDeathBreath=70;enemy.IsBot=true;
            check(Transport(enemy).Actors[0].IsKnocked,"native bot uses the same confirmed knockdown lifecycle as a human player");

            FrameBuffer buffer=new FrameBuffer();DateTime at=DateTime.UtcNow;
            alive.CapturedUtc=at;knocked.CapturedUtc=at.AddMilliseconds(20);buffer.Push(alive);buffer.Push(knocked);
            Actor transition=buffer.SampleAt(at.AddMilliseconds(10)).Actors[0];
            check(transition.IsKnocked&&transition.Health==0&&transition.NearDeathBreath==73.5f,"interpolated positions use the newer discrete knockdown state immediately");
            revived.CapturedUtc=at.AddMilliseconds(40);buffer.Push(revived);
            transition=buffer.SampleAt(at.AddMilliseconds(30)).Actors[0];
            check(!transition.IsKnocked&&transition.Health==25,"interpolating across revival immediately removes the old knockdown state");
            finished.CapturedUtc=at.AddMilliseconds(60);buffer.Push(finished);
            check(buffer.SampleAt(at.AddMilliseconds(50)).Actors.Length==0,"interpolation does not resurrect a finished opponent from an older frame");

            options=new RenderOptions { PlayerCountVisible=false,Names=false,BotLabels=false,Distance=false,Health=false,Alerts=false,Lines=true,Boxes=true };
            options.Visibility.Enabled=true;
            options.Visibility.VisibleColorArgb=Color.FromArgb(18,207,131).ToArgb();
            options.Visibility.OccludedColorArgb=Color.FromArgb(83,121,229).ToArgb();
            using(Bitmap bitmap=new Bitmap(800,450))using(Graphics graphics=Graphics.FromImage(bitmap))
            {
                knocked.CapturedUtc=DateTime.UtcNow;knocked.Actors[0].IsVisible=false;
                graphics.Clear(Color.Black);Painter.Draw(graphics,bitmap.Size,knocked,options,false,false);
                check(Pixels(bitmap,new Rectangle(397,70,7,95),Painter.KnockedColor)>70,"actual knocked ESP line is red even when visibility colors would choose the occluded palette");
                check(Pixels(bitmap,new Rectangle(380,198,42,80),Color.FromArgb(options.Visibility.OccludedColorArgb))>70,"knocked line override preserves the configured box visibility color");
                check(Pixels(bitmap,new Rectangle(320,174,160,25),Painter.KnockedColor)>10,"KNOCKED status remains visible with ordinary names and bot labels disabled");
                knocked.Actors[0].IsVisible=true;
                graphics.Clear(Color.Black);Painter.Draw(graphics,bitmap.Size,knocked,options,false,false);
                check(Pixels(bitmap,new Rectangle(397,70,7,95),Painter.KnockedColor)>70,"visible-state changes cannot replace the knocked line's red highlight");
                revived.CapturedUtc=DateTime.UtcNow;revived.Actors[0].IsVisible=true;
                graphics.Clear(Color.Black);Painter.Draw(graphics,bitmap.Size,revived,options,false,false);
                check(Pixels(bitmap,new Rectangle(397,70,7,95),Painter.KnockedColor)==0&&Pixels(bitmap,new Rectangle(397,70,7,95),Color.FromArgb(options.Visibility.VisibleColorArgb))>70,"revival restores the normal line color without a lingering red marker");
                finished.CapturedUtc=DateTime.UtcNow;
                graphics.Clear(Color.Black);Painter.Draw(graphics,bitmap.Size,finished,options,false,false);
                check(Pixels(bitmap,new Rectangle(397,70,7,95),Painter.KnockedColor)==0,"finished opponent leaves no red ESP line behind");
            }
        }
    }
}
