using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ESPWorkbench
{
    public struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public bool Finite { get { return Good(X) && Good(Y) && Good(Z); } }
        public static bool Good(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }
        public float Distance(Vec3 b) { double x = X - b.X, y = Y - b.Y, z = Z - b.Z; return (float)Math.Sqrt(x*x + y*y + z*z); }
    }
    public static class Projection
    {
        public static bool ProjectCamera(Vec3 world, Vec3 origin, Vec3 rotation, float fov, Size size, out PointF point)
        {
            point=PointF.Empty;
            if (!world.Finite || !origin.Finite || !rotation.Finite || !Vec3.Good(fov) || fov<5 || fov>175 || size.Width<1 || size.Height<1) return false;
            double pitch=rotation.X*Math.PI/180, yaw=rotation.Y*Math.PI/180, roll=rotation.Z*Math.PI/180;
            double sp=Math.Sin(pitch),cp=Math.Cos(pitch),sy=Math.Sin(yaw),cy=Math.Cos(yaw),sr=Math.Sin(roll),cr=Math.Cos(roll);
            double x=world.X-origin.X,y=world.Y-origin.Y,z=world.Z-origin.Z;
            double forward=x*cp*cy+y*cp*sy+z*sp;
            if (forward<1) return false;
            double right=x*(sr*sp*cy-cr*sy)+y*(sr*sp*sy+cr*cy)-z*sr*cp;
            double up=-x*(cr*sp*cy+sr*sy)+y*(cy*sr-cr*sp*sy)+z*cr*cp;
            double focal=size.Width/(2*Math.Tan(fov*Math.PI/360));
            point=new PointF((float)(size.Width/2.0+right*focal/forward),(float)(size.Height/2.0-up*focal/forward));
            return Vec3.Good(point.X)&&Vec3.Good(point.Y);
        }
        // Matrix memory order is explicit. World Z is up; clipping uses Direct3D depth [0,w].
        public static bool Project(Vec3 p, float[] m, bool columnMajor, Size size, out PointF point)
        {
            point = PointF.Empty;
            if (m == null || m.Length != 16 || !p.Finite || size.Width <= 0 || size.Height <= 0) return false;
            float x, y, z, w;
            if (!columnMajor)
            {
                x = p.X*m[0]+p.Y*m[4]+p.Z*m[8]+m[12]; y = p.X*m[1]+p.Y*m[5]+p.Z*m[9]+m[13];
                z = p.X*m[2]+p.Y*m[6]+p.Z*m[10]+m[14]; w = p.X*m[3]+p.Y*m[7]+p.Z*m[11]+m[15];
            }
            else
            {
                x = p.X*m[0]+p.Y*m[1]+p.Z*m[2]+m[3]; y = p.X*m[4]+p.Y*m[5]+p.Z*m[6]+m[7];
                z = p.X*m[8]+p.Y*m[9]+p.Z*m[10]+m[11]; w = p.X*m[12]+p.Y*m[13]+p.Z*m[14]+m[15];
            }
            if (!Vec3.Good(w) || w < 0.001f || !Vec3.Good(x) || !Vec3.Good(y) || !Vec3.Good(z) || z < 0 || z > w) return false;
            point = new PointF((1+x/w)*size.Width/2f, (1-y/w)*size.Height/2f);
            return Vec3.Good(point.X) && Vec3.Good(point.Y);
        }
    }
    public sealed class Actor
    {
        public string Name,Address,Kind,LootGroup,ItemId;
        public Vec3 Position;
        public float Health, MaxHealth, Height;
        public float Fuel,MaxFuel;
        public bool HasHealth,HasFuel;
        public bool? IsBot;
        public int Quantity;
        public int Team;
    }
    public sealed class Frame
    {
        public DateTime CapturedUtc = DateTime.UtcNow;
        public float[] Matrix;
        public bool ColumnMajor;
        public bool CameraMode;
        public Vec3 CameraPosition, CameraRotation;
        public float Fov;
        public int TotalActorCount;
        public Vec3 Origin;
        public float UnitsPerMeter = 100;
        public int LocalTeam = -1;
        public bool? LocalScoped,LocalFiring;
        public Actor[] Actors = new Actor[0];
        public string Source = "No data";
        public double ReadMilliseconds;
        public double ReadHertz;
        public bool Project(Vec3 position,Size viewport,out PointF point)
        {
            return CameraMode ? Projection.ProjectCamera(position,CameraPosition,CameraRotation,Fov,viewport,out point) : Projection.Project(position,Matrix,ColumnMajor,viewport,out point);
        }
    }
    public sealed class FrameBuffer
    {
        private readonly object gate=new object();
        private readonly List<Frame> frames=new List<Frame>();
        public void Push(Frame frame) { lock(gate) { frames.Add(frame); if(frames.Count>8) frames.RemoveAt(0); } }
        public void Clear() { lock(gate) frames.Clear(); }
        public Frame Sample() { return SampleAt(DateTime.UtcNow.AddMilliseconds(-25)); }
        public Frame SampleAt(DateTime time)
        {
            lock(gate)
            {
                if(frames.Count==0) return null;
                Frame first=frames[0],last=frames[frames.Count-1];
                if(!last.CameraMode||time>=last.CapturedUtc) return last;
                if(time<=first.CapturedUtc) return first;
                Frame a=first,b=last;
                for(int i=1;i<frames.Count;i++) if(frames[i].CapturedUtc>=time) { a=frames[i-1]; b=frames[i]; break; }
                double duration=(b.CapturedUtc-a.CapturedUtc).TotalMilliseconds;
                if(duration<=0||duration>250) return b;
                float t=(float)Math.Max(0,Math.Min(1,(time-a.CapturedUtc).TotalMilliseconds/duration));
                Dictionary<string,Actor> prior=new Dictionary<string,Actor>();
                foreach(Actor actor in a.Actors) if(!String.IsNullOrEmpty(actor.Address)) prior[actor.Address]=actor;
                List<Actor> actors=new List<Actor>();
                foreach(Actor actor in b.Actors)
                {
                    Actor previous;
                    if(actor.Kind=="Loot"||actor.Kind=="Crate"||String.IsNullOrEmpty(actor.Address)||!prior.TryGetValue(actor.Address,out previous)) { actors.Add(actor); continue; }
                    actors.Add(new Actor { Address=actor.Address,Kind=actor.Kind,LootGroup=actor.LootGroup,ItemId=actor.ItemId,Quantity=actor.Quantity,IsBot=actor.IsBot,HasHealth=actor.HasHealth,HasFuel=actor.HasFuel,Fuel=actor.Fuel,MaxFuel=actor.MaxFuel,Name=actor.Name,Position=Lerp(previous.Position,actor.Position,t),Height=previous.Height+(actor.Height-previous.Height)*t,Health=actor.Health,MaxHealth=actor.MaxHealth,Team=actor.Team });
                }
                return new Frame { CapturedUtc=b.CapturedUtc,CameraMode=true,CameraPosition=Lerp(a.CameraPosition,b.CameraPosition,t),CameraRotation=new Vec3(Angle(a.CameraRotation.X,b.CameraRotation.X,t),Angle(a.CameraRotation.Y,b.CameraRotation.Y,t),Angle(a.CameraRotation.Z,b.CameraRotation.Z,t)),Fov=a.Fov+(b.Fov-a.Fov)*t,Origin=Lerp(a.Origin,b.Origin,t),UnitsPerMeter=b.UnitsPerMeter,LocalTeam=b.LocalTeam,LocalScoped=b.LocalScoped,LocalFiring=b.LocalFiring,Actors=actors.ToArray(),Source=b.Source,TotalActorCount=b.TotalActorCount,ReadMilliseconds=b.ReadMilliseconds,ReadHertz=b.ReadHertz };
            }
        }
        private static Vec3 Lerp(Vec3 a,Vec3 b,float t) { return new Vec3(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t); }
        private static float Angle(float a,float b,float t) { float difference=(b-a)%360; if(difference>180) difference-=360; if(difference< -180) difference+=360; return a+difference*t; }
    }
    public sealed class RenderOptions
    {
        public AimOptions Aim=new AimOptions();
        public ShortcutOptions Shortcuts=new ShortcutOptions();
        public bool EspEnabled=true,QuickHudVisible=true;
        public bool Boxes = true, Names = true, Health = true, Distance = true, Lines = true, Alerts = true, Allies;
        public float MaxDistance = 1500;
        public int RefreshRate=120,LabelLimit=70;
        public float FontSize=9,LineWidth=1.5f;
        public bool BotLabels=true,ShowBots=true,ShowPlayers=true,ShowUnknownPlayers=true,Fuel=true,Quantities=true,ShowItemIds;
        public bool LootWeapons=true,LootAmmo=true,LootHealing=true,LootArmor=true,LootOptics=true,LootEquipment=true,LootOther=false;
        public CategoryOptions Players=new CategoryOptions { ColorArgb=unchecked((int)0xFF32DCBA) };
        public CategoryOptions Training=new CategoryOptions { MaxDistance=500,MaxVisible=16,ColorArgb=unchecked((int)0xFF66D9EF) };
        public CategoryOptions Vehicles=new CategoryOptions { MaxDistance=800,MaxVisible=24,Lines=false,Alerts=false,ColorArgb=unchecked((int)0xFF70A5FF) };
        public CategoryOptions Loot=new CategoryOptions { Enabled=false,MaxDistance=100,MaxVisible=25,Lines=false,Alerts=false,Health=false,Boxes=false,ColorArgb=unchecked((int)0xFFFFD166) };
        public CategoryOptions Crates=new CategoryOptions { MaxDistance=300,MaxVisible=20,Lines=false,Alerts=false,Health=false,Boxes=false,ColorArgb=unchecked((int)0xFFD99BFF) };
        public CategoryOptions Category(string kind) { if(kind=="Vehicle") return Vehicles; if(kind=="Loot") return Loot; if(kind=="Crate") return Crates; if(kind=="TrainingTarget") return Training; return Players; }
        public void EnsureDefaults()
        {
            Aim=Aim??new AimOptions(); Aim.EnsureDefaults();
            Shortcuts=Shortcuts??new ShortcutOptions(); Shortcuts.EnsureDefaults();
            RenderOptions defaults=new RenderOptions(); Players=Players??defaults.Players;Training=Training??defaults.Training;Vehicles=Vehicles??defaults.Vehicles;Loot=Loot??defaults.Loot;Crates=Crates??defaults.Crates;
            RefreshRate=Math.Max(30,Math.Min(120,RefreshRate));LabelLimit=Math.Max(10,Math.Min(200,LabelLimit)); FontSize=Vec3.Good(FontSize)?Math.Max(7,Math.Min(18,FontSize)):9; LineWidth=Vec3.Good(LineWidth)?Math.Max(1,Math.Min(4,LineWidth)):1.5f;
            foreach(CategoryOptions c in new[]{Players,Training,Vehicles,Loot,Crates}) { c.MaxVisible=Math.Max(1,Math.Min(500,c.MaxVisible)); c.MaxDistance=Vec3.Good(c.MaxDistance)?Math.Max(10,Math.Min(5000,c.MaxDistance)):500; }
        }
        public bool Include(Actor actor)
        {
            if(!Category(actor.Kind).Enabled) return false;
            if(actor.Kind=="Character") { if(actor.IsBot==true&&!ShowBots)return false;if(actor.IsBot==false&&!ShowPlayers)return false;if(!actor.IsBot.HasValue&&!ShowUnknownPlayers)return false; }
            if(actor.Kind=="Loot")
            {
                if(actor.LootGroup=="Weapon")return LootWeapons;if(actor.LootGroup=="Ammo")return LootAmmo;if(actor.LootGroup=="Healing")return LootHealing;if(actor.LootGroup=="Armor")return LootArmor;if(actor.LootGroup=="Optic")return LootOptics;if(actor.LootGroup=="Equipment")return LootEquipment;return LootOther;
            }
            return true;
        }
    }
    public sealed class CategoryOptions
    {
        public bool Enabled=true,Boxes=true,Names=true,Health=true,Distance=true,Lines=true,Alerts=true;
        public float MaxDistance=500;
        public int MaxVisible=100,ColorArgb=unchecked((int)0xFF32DCBA);
    }
    public sealed class EdgeContact
    {
        public int Sector,Count=1;
        public float Distance;
        public double Bearing;
        public string Direction;
    }
    public static class Threats
    {
        public static EdgeContact Contact(Vec3 position,Frame frame,Size viewport)
        {
            if (!frame.CameraMode || !position.Finite) return null;
            PointF screen;
            if (frame.Project(position,viewport,out screen)&&screen.X>=20&&screen.X<=viewport.Width-20&&screen.Y>=20&&screen.Y<=viewport.Height-20) return null;
            double yaw=frame.CameraRotation.Y*Math.PI/180;
            double x=position.X-frame.CameraPosition.X,y=position.Y-frame.CameraPosition.Y;
            double forward=x*Math.Cos(yaw)+y*Math.Sin(yaw),right=-x*Math.Sin(yaw)+y*Math.Cos(yaw);
            double bearing=Math.Atan2(right,forward);
            int sector=((int)Math.Round(bearing/(Math.PI/4))+8)%8;
            string[] directions={"FRONT","FRONT RIGHT","RIGHT","BACK RIGHT","BEHIND","BACK LEFT","LEFT","FRONT LEFT"};
            return new EdgeContact { Sector=sector,Bearing=bearing,Direction=directions[sector],Distance=position.Distance(frame.Origin)/frame.UnitsPerMeter };
        }
    }
    public sealed class Profile
    {
        public string Name = "Unconfigured profile";
        public bool Enabled;
        public string Package = "com.tencent.ig", VersionName = "", VersionCode = "", Module = "libUE4.so", Backend="AdbPages", ModuleSha256="";
        public int PointerSize = 8, MaxActors = 1024, MaxDisplayed = 100, PollMilliseconds = 500;
        public bool ColumnMajor;
        public float UnitsPerMeter = 100, ActorHeight = 180, MaxHealth = 100;
        public string[] ActorDataPath, ActorCountPath, MatrixPath, OriginPath, LocalTeamPath;
        public string[] PositionPath, HealthPath, TeamPath, PlayerMarkerPath;
        public int PlayerMarkerValue;
        public static ulong Offset(string s)
        {
            if (String.IsNullOrWhiteSpace(s)) throw new InvalidDataException("Empty offset.");
            return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? UInt64.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) : UInt64.Parse(s, CultureInfo.InvariantCulture);
        }
        public static bool Has(string[] p) { return p != null && p.Length > 0; }
        public void Validate()
        {
            if (!Enabled) throw new InvalidDataException("Profile is disabled: verified version-specific offsets are required. The supplied template is NOT a working PUBG profile.");
            if (!Regex.IsMatch(Package ?? "", @"^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+$")) throw new InvalidDataException("Invalid package name.");
            if (String.IsNullOrWhiteSpace(VersionName) || String.IsNullOrWhiteSpace(VersionCode)) throw new InvalidDataException("Both exact VersionName and VersionCode are required.");
            if (!Regex.IsMatch(Module ?? "", @"^[a-zA-Z0-9_.-]+\.so$")) throw new InvalidDataException("Invalid library name.");
            if (PointerSize != 4 && PointerSize != 8) throw new InvalidDataException("PointerSize must be 4 or 8.");
            if (MaxActors < 1 || MaxActors > 16384 || MaxDisplayed < 1 || MaxDisplayed > 200 || PollMilliseconds < 100 || PollMilliseconds > 10000) throw new InvalidDataException("Invalid actor or polling limits.");
            if (!Vec3.Good(UnitsPerMeter) || UnitsPerMeter <= 0 || !Vec3.Good(ActorHeight) || ActorHeight <= 0 || !Vec3.Good(MaxHealth) || MaxHealth <= 0) throw new InvalidDataException("Invalid units, height, or health.");
            if (Backend=="Native")
            {
                bool known=(VersionCode=="21525"&&ModuleSha256=="abb9343f69735378b6aea4db97a66e9789664075b0196b1b99378514052454ce")
                    || (VersionCode=="21518"&&ModuleSha256=="ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484");
                if (Package!="com.tencent.ig" || VersionName!="4.6.0" || Module!="libUE4.so" || PointerSize!=8 || !known) throw new InvalidDataException("Native reader requires an exact inspected PUBG Global 4.6.0 ARM64 build/library pair (21525 or 21518).");
                return;
            }
            if (Backend!="AdbPages") throw new InvalidDataException("Unknown reader backend.");
            foreach (string[] path in new[] { ActorDataPath, ActorCountPath, MatrixPath, OriginPath, PositionPath, HealthPath, TeamPath })
                if (!Has(path)) throw new InvalidDataException("Required pointer paths are missing.");
            foreach (string[] path in new[] { ActorDataPath, ActorCountPath, MatrixPath, OriginPath, LocalTeamPath, PositionPath, HealthPath, TeamPath, PlayerMarkerPath })
                if (Has(path)) { if (path.Length > 16) throw new InvalidDataException("Pointer path is too long."); foreach (string offset in path) Offset(offset); }
        }
        public static Profile Load(string path)
        {
            if (new FileInfo(path).Length > 128*1024) throw new InvalidDataException("Profile exceeds 128 KB.");
            Profile p = new JavaScriptSerializer().Deserialize<Profile>(File.ReadAllText(path));
            if (p == null) throw new InvalidDataException("Empty profile.");
            return p;
        }
        public static Profile ForInstalled(GameInfo info)
        {
            if(!Regex.IsMatch(info.VersionName??"",@"^[0-9]+(\.[0-9]+)*$")||!Regex.IsMatch(info.VersionCode??"",@"^[0-9]+$")) throw new InvalidDataException("Invalid installed game version.");
            string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles","pubg-global-"+info.VersionName+"-"+info.VersionCode+".json");
            Profile profile=Load(file); profile.Validate(); return profile;
        }
    }
    public sealed class MapRegion
    {
        public ulong Start, End, FileOffset;
        public string Permissions, Path;
        public static List<MapRegion> Parse(string text)
        {
            List<MapRegion> maps = new List<MapRegion>();
            foreach (string line in text.Split('\n'))
            {
                Match m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]+)-([0-9a-fA-F]+)\s+(\S+)\s+([0-9a-fA-F]+)\s+\S+\s+\d+\s*(.*)$");
                if (!m.Success) continue;
                maps.Add(new MapRegion { Start = Profile.Offset("0x"+m.Groups[1].Value), End = Profile.Offset("0x"+m.Groups[2].Value), Permissions = m.Groups[3].Value, FileOffset = Profile.Offset("0x"+m.Groups[4].Value), Path = m.Groups[5].Value });
            }
            return maps;
        }
        public static ulong ModuleBase(List<MapRegion> maps, string module)
        {
            MapRegion[] found = maps.Where(m => (m.Path.EndsWith("/"+module, StringComparison.Ordinal) || m.Path.EndsWith("/"+module+" (deleted)", StringComparison.Ordinal)) && m.Start >= m.FileOffset).ToArray();
            if (found.Length == 0) throw new InvalidDataException("Library not mapped: "+module+". Verify the installed build; no base address is guessed.");
            return found.Min(m => m.Start-m.FileOffset);
        }
    }
}
