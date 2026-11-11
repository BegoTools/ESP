using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace ESPWorkbench
{
    internal sealed class FixtureMemory : IMemory
    {
        private readonly Dictionary<ulong,byte> bytes=new Dictionary<ulong,byte>();
        public int ResetCount;
        public void NewFrame() { ResetCount++; }
        public void Put(ulong address,byte[] values) { for (int i=0;i<values.Length;i++) bytes[address+(ulong)i]=values[i]; }
        public byte[] Read(ulong address,int count)
        {
            byte[] result=new byte[count]; for (int i=0;i<count;i++) if (!bytes.TryGetValue(address+(ulong)i,out result[i])) throw new IOException("Unmapped fixture memory."); return result;
        }
    }
    public static class SelfTests
    {
        private static readonly List<string> results=new List<string>();
        private static void Check(bool condition,string name) { if (!condition) throw new Exception("Assertion failed: "+name); results.Add("PASS "+name); }
        private static void Throws(Action action,string name) { bool thrown=false; try { action(); } catch (InvalidDataException) { thrown=true; } catch (IOException) { thrown=true; } Check(thrown,name); }
        private static void PutVector(FixtureMemory m,ulong a,float x,float y,float z) { m.Put(a,BitConverter.GetBytes(x).Concat(BitConverter.GetBytes(y)).Concat(BitConverter.GetBytes(z)).ToArray()); }
        public static string Run()
        {
            results.Clear();
            AimTests.Run(Check);
            QuickTests.Run(Check);
            Settings legacy=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>("{\"AdbPath\":\"ld-adb\",\"ConsolePath\":\"ld-console\",\"Serial\":\"emulator-5554\"}");
            Check(legacy.Connection(EmulatorKind.LDPlayer).AdbPath=="ld-adb"&&legacy.LDPlayer.Serial=="emulator-5554","legacy LDPlayer connection settings migrate");
            legacy.Connection(EmulatorKind.GameLoop).AdbPath="gl-adb"; legacy.GameLoop.Serial="127.0.0.1:5555";legacy.LDPlayer.ProfilePath="ld-profile";legacy.GameLoop.ProfilePath="gl-profile";
            Settings savedConnections=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Settings>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(legacy));
            Check(savedConnections.LDPlayer.AdbPath=="ld-adb"&&savedConnections.GameLoop.AdbPath=="gl-adb"&&savedConnections.LDPlayer.Serial=="emulator-5554","emulator connections persist independently");
            Check(savedConnections.LDPlayer.ProfilePath=="ld-profile"&&savedConnections.GameLoop.ProfilePath=="gl-profile","emulator build profiles persist independently");
            Check(Adb.BuildRootCommand(PrivilegeMode.Su,"id",true)=="su -c 'id'","LDPlayer su command remains unchanged");
            Check(Adb.BuildRootCommand(PrivilegeMode.Txperm,"id",false)=="/system/xbin/txperm 0 /system/bin/sh -c 'id'","GameLoop uses its existing privileged shell");
            string guarded=Adb.BuildRootCommand(PrivilegeMode.Txperm,"reader --once",true);
            Check(guarded.Contains("ESP_RESTORE_ENFORCING=1")&&guarded.Contains("trap")&&guarded.Contains("sleep 2")&&guarded.Contains("reader --once"),"GameLoop process bootstrap includes native restore and independent recovery guard");
            Throws(()=>Adb.BuildRootCommand(PrivilegeMode.Unavailable,"id",true),"missing privilege cannot execute process access command");
            Profile knownNative=new Profile {Enabled=true,Backend="Native",VersionName="4.6.0",VersionCode="21518",ModuleSha256="ad923890b4e12e389aba66253ef0259285b351b3742fbda7ae0d0716ea070484"};knownNative.Validate();
            Check(true,"verified GameLoop exact-build profile accepted");
            knownNative.VersionCode="21525";Throws(()=>knownNative.Validate(),"native build and hash cannot be interchanged");
            Check(Emulators.ValidateEndpoint("localhost:5555")=="127.0.0.1:5555","GameLoop local ADB address normalization");
            Throws(()=>Emulators.ValidateEndpoint("127.0.0.1:65536"),"invalid GameLoop port rejected");
            Throws(()=>Emulators.ValidateEndpoint("example.com:5555"),"non-local GameLoop address rejected");
            Check(Emulators.IsRenderSurface("subWin","sub")&&!Emulators.IsRenderSurface("Qt5152QWindowIcon","GameLoopEmulator"),"GameLoop rendering child excludes Qt chrome");
            Profile compatibleProfile=new Profile { VersionName="4.6.0",VersionCode="21525",PointerSize=8 };
            GameInfo compatibleInfo=new GameInfo { VersionName="4.6.0",VersionCode="21525",Abi="arm64-v8a",Root=true };
            Check(GameCompatibility.Problems(compatibleProfile,compatibleInfo).Count==0,"existing LDPlayer reader prerequisites accepted");
            compatibleInfo.VersionCode="21518"; compatibleInfo.Root=false;
            List<string> blocked=GameCompatibility.Problems(compatibleProfile,compatibleInfo);
            Check(blocked.Count==2&&blocked[0].Contains("21518")&&blocked[1].Contains("Root"),"build and root blockers reported together");
            Throws(()=>GameCompatibility.Ensure(compatibleProfile,compatibleInfo,"GameLoop"),"incompatible GameLoop cannot start reader");
            PointF point; Size viewport=new Size(1000,600); float[] identity=new float[] { 1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1 };
            Check(Projection.Project(new Vec3(0,0,.5f),identity,false,viewport,out point)&&point==new PointF(500,300),"projection center");
            Check(Projection.Project(new Vec3(1,1,.5f),identity,false,viewport,out point)&&point==new PointF(1000,0),"projection corner");
            Check(!Projection.Project(new Vec3(0,0,-1),identity,false,viewport,out point),"near-plane rejection");
            Check(!Projection.Project(new Vec3(0,0,2),identity,false,viewport,out point),"far-plane rejection");
            Check(!Projection.Project(new Vec3(float.NaN,0,0),identity,false,viewport,out point),"NaN rejection");
            float[] wzero=(float[])identity.Clone(); wzero[15]=0;
            Check(!Projection.Project(new Vec3(0,0,0),wzero,false,viewport,out point),"zero-W rejection");
            float[] transform=(float[])identity.Clone(); transform[12]=.25f; transform[13]=-.5f;
            float[] transposed=new float[16]; for (int r=0;r<4;r++) for (int c=0;c<4;c++) transposed[r*4+c]=transform[c*4+r];
            PointF other; Check(Projection.Project(new Vec3(0,0,.5f),transform,false,viewport,out point)&&Projection.Project(new Vec3(0,0,.5f),transposed,true,viewport,out other)&&point==other,"matrix order conventions");
            List<MapRegion> maps=MapRegion.Parse("400000-401000 r--p 00000000 08:12 42 /data/app/some path/libUE4.so\n401000-403000 rw-p 00001000 08:12 42 /data/app/some path/libUE4.so\nbad line\n500000-501000 rw-p 00000000 00:00 0\n");
            Check(maps.Count==3&&maps[0].Path=="/data/app/some path/libUE4.so","proc maps parsing");
            Check(MapRegion.ModuleBase(maps,"libUE4.so")==0x400000,"ASLR module base");
            Throws(()=>MapRegion.ModuleBase(maps,"libAbsent.so"),"missing module fails explicitly");
            List<Device> devices=Adb.ParseDevices("List of devices attached\r\nemulator-5554 device product:test model:Android\r\n127.0.0.1:5557 offline\nABC unauthorized\n");
            Check(devices.Count==3&&devices[0].Serial=="emulator-5554"&&devices[2].State=="unauthorized","ADB serial and state parsing");
            Check(Commands.Quote("C:\\path with space\\")=="\"C:\\path with space\\\\\"","Windows trailing slash quoting");
            Check(Commands.Quote("a\"b")=="\"a\\\"b\"","Windows embedded quote escaping");
            Throws(()=>new Profile().Validate(),"disabled template fails closed");
            Profile p=new Profile { Enabled=true,Name="fixture",VersionName="TEST",VersionCode="1",ActorDataPath=new[] { "0x100" },ActorCountPath=new[] { "0x108" },MatrixPath=new[] { "0x200" },OriginPath=new[] { "0x300" },LocalTeamPath=new[] { "0x310" },PositionPath=new[] { "0x20","0x10" },HealthPath=new[] { "0x40" },TeamPath=new[] { "0x44" } };
            p.Validate();
            FixtureMemory memory=new FixtureMemory(); const ulong root=0x100000, array=0x200000, actor=0x300000, component=0x400000;
            memory.Put(root+0x100,BitConverter.GetBytes(array)); memory.Put(root+0x108,BitConverter.GetBytes(3));
            float[] demo=Demo.Make(0).Matrix; byte[] matrixBytes=new byte[64]; Buffer.BlockCopy(demo,0,matrixBytes,0,64); memory.Put(root+0x200,matrixBytes);
            PutVector(memory,root+0x300,0,0,160); memory.Put(root+0x310,BitConverter.GetBytes(1));
            memory.Put(array,BitConverter.GetBytes(actor).Concat(BitConverter.GetBytes(0UL)).Concat(BitConverter.GetBytes(0x700000UL)).ToArray());
            memory.Put(actor+0x20,BitConverter.GetBytes(component)); PutVector(memory,component+0x10,100,1200,0); memory.Put(actor+0x40,BitConverter.GetBytes(75f)); memory.Put(actor+0x44,BitConverter.GetBytes(2));
            GameReader reader=new GameReader(p,memory,root); Frame frame=reader.Read(CancellationToken.None);
            Check(frame.Actors.Length==1&&frame.Actors[0].Health==75&&frame.Actors[0].Team==2,"reader skips null and despawned actors");
            Check(frame.Actors[0].Position.X==100&&frame.Actors[0].Position.Y==1200,"nested pointer path resolves final field address");
            Check(frame.LocalTeam==1&&frame.Origin.Z==160&&memory.ResetCount==1,"local state and frame cache reset");
            memory.Put(root+0x108,BitConverter.GetBytes(999999)); Throws(()=>reader.Read(CancellationToken.None),"actor count bounds");
            memory.Put(root+0x108,BitConverter.GetBytes(0)); Check(reader.Read(CancellationToken.None).Actors.Length==0,"empty actor array");
            FixtureMemory memory32=new FixtureMemory(); memory32.Put(0x1000,BitConverter.GetBytes((uint)0x2000));
            Check(new MemoryValues(memory32,4).Resolve(0x1000,new[] { "0x0","0x10" })==0x2010,"32-bit pointer path");
            CancellationTokenSource cancel=new CancellationTokenSource(); cancel.Cancel(); bool cancelled=false;
            memory.Put(root+0x108,BitConverter.GetBytes(3)); try { reader.Read(cancel.Token); } catch (OperationCanceledException) { cancelled=true; } cancel.Dispose(); Check(cancelled,"reader cancellation");
            Check(!Painter.Fresh(new Frame { CapturedUtc=DateTime.UtcNow.AddSeconds(-3) }),"stale frames hidden");
            Check(Painter.Fresh(frame),"fresh frame eligible");
            Frame demoFrame=Demo.Make(0); Check(demoFrame.Actors.All(a=>a.Name.StartsWith("DEMO"))&&demoFrame.Source.Contains("synthetic"),"synthetic scene explicitly labeled");
            Check(Projection.Project(demoFrame.Actors[0].Position,demoFrame.Matrix,false,viewport,out point),"demo matrix projects actors");
            Check(Projection.ProjectCamera(new Vec3(100,0,0),new Vec3(),new Vec3(),90,viewport,out point)&&Math.Abs(point.X-500)<.01&&Math.Abs(point.Y-300)<.01,"camera forward projects to center");
            Check(Projection.ProjectCamera(new Vec3(100,100,0),new Vec3(),new Vec3(),90,viewport,out point)&&Math.Abs(point.X-1000)<.01,"camera horizontal FOV projection");
            Check(Projection.ProjectCamera(new Vec3(0,100,0),new Vec3(),new Vec3(0,90,0),90,viewport,out point)&&Math.Abs(point.X-500)<.01,"camera yaw convention");
            Check(Projection.ProjectCamera(new Vec3(100,0,100),new Vec3(),new Vec3(45,0,0),90,viewport,out point)&&Math.Abs(point.Y-300)<.01,"camera pitch convention");
            Check(!Projection.ProjectCamera(new Vec3(-100,0,0),new Vec3(),new Vec3(),90,viewport,out point),"camera rejects points behind view");
            Check(!Projection.ProjectCamera(new Vec3(100,0,0),new Vec3(),new Vec3(),float.NaN,viewport,out point),"camera rejects invalid FOV");
            Frame threatFrame=new Frame { CameraMode=true,CameraPosition=new Vec3(),CameraRotation=new Vec3(),Origin=new Vec3(),Fov=90 };
            Check(Threats.Contact(new Vec3(10000,0,0),threatFrame,viewport)==null,"visible contact does not trigger edge warning");
            EdgeContact behind=Threats.Contact(new Vec3(-10000,0,0),threatFrame,viewport);
            Check(behind!=null&&behind.Sector==4&&Math.Abs(behind.Distance-100)<.01,"behind warning direction and metres");
            EdgeContact right=Threats.Contact(new Vec3(0,10000,0),threatFrame,viewport);
            Check(right!=null&&right.Sector==2,"right warning direction");
            EdgeContact left=Threats.Contact(new Vec3(0,-10000,0),threatFrame,viewport);
            Check(left!=null&&left.Sector==6,"left warning direction");
            FrameBuffer buffer=new FrameBuffer(); DateTime moment=DateTime.UtcNow;
            buffer.Push(new Frame { CameraMode=true,CapturedUtc=moment,CameraRotation=new Vec3(0,179,0),Actors=new[]{new Actor { Address="one",Position=new Vec3(0,0,0),Height=180 }} });
            buffer.Push(new Frame { CameraMode=true,CapturedUtc=moment.AddMilliseconds(20),CameraRotation=new Vec3(0,-179,0),Actors=new[]{new Actor { Address="one",Position=new Vec3(100,0,0),Height=180 }} });
            Frame interpolated=buffer.SampleAt(moment.AddMilliseconds(10));
            Check(Math.Abs(interpolated.CameraRotation.Y-180)<.01,"camera interpolation follows shortest angle across wrap");
            Check(Math.Abs(interpolated.Actors[0].Position.X-50)<.01,"actor interpolation matches stable address");
            buffer.Clear(); Check(buffer.Sample()==null,"frame buffer clears on stop");
            RenderOptions filter=new RenderOptions();
            Actor lootActor=new Actor { Kind="Loot",LootGroup="Ammo" }; Check(!filter.Include(lootActor),"loot category is opt-in"); filter.Loot.Enabled=true;Check(filter.Include(lootActor),"loot enabled with category filter");filter.LootAmmo=false;Check(!filter.Include(lootActor),"ammo toggles independently");
            filter.ShowBots=false;Check(!filter.Include(new Actor { Kind="Character",IsBot=true }),"bot filter leaves players independent");Check(filter.Include(new Actor { Kind="Character",IsBot=false }),"player remains enabled when bots hidden");
            filter.Vehicles.Enabled=false;Check(!filter.Include(new Actor { Kind="Vehicle" }),"vehicle toggle filters category");
            string saved=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(filter);RenderOptions restored=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<RenderOptions>(saved);restored.EnsureDefaults();Check(!restored.LootAmmo&&!restored.ShowBots&&restored.RefreshRate==120,"menu settings round-trip persistence");
            using(Bitmap canvas=new Bitmap(viewport.Width,viewport.Height))using(Graphics graphics=Graphics.FromImage(canvas))
            {
                Frame close=new Frame { CameraMode=true,CameraPosition=new Vec3(),CameraRotation=new Vec3(),Origin=new Vec3(),Fov=5,Actors=new[]{new Actor { Kind="Character",Position=new Vec3(100,0,0),Height=180,Health=100,MaxHealth=100,Team=2 }} };
                Painter.Draw(graphics,viewport,close,new RenderOptions { Names=false,Distance=false,Lines=false,Alerts=false },true);
                Color boundary=canvas.GetPixel(1,200); Check(boundary.G>100,"nearby zoomed character retains clipped box");
            }
            return String.Join(Environment.NewLine,results)+Environment.NewLine+results.Count+" checks passed. Synthetic memory fixtures; no assertion of PUBG layout correctness.";
        }
    }
}
