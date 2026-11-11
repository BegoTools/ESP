using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ESPWorkbench;
internal static class StreamValidation
{
    private static int Main(string[] args)
    {
        using(CancellationTokenSource stop=new CancellationTokenSource())
        {
            try
            {
                Adb adb=new Adb(args[0],args[1]);
                GameInfo info=GameInfo.Inspect(adb,"com.tencent.ig",true,CancellationToken.None);
                Profile profile=Profile.ForInstalled(info);GameCompatibility.Ensure(profile,info,"GameLoop");
                List<MapRegion> maps=MapRegion.Parse(info.Maps);ulong module=MapRegion.ModuleBase(maps,profile.Module);
                NativeReader.Prepare(adb,profile,info,maps,CancellationToken.None);
                List<double> times=new List<double>(),work=new List<double>();List<int> counts=new List<int>();
                Frame prior=null;int cameraChanges=0,positionChanges=0;Stopwatch clock=Stopwatch.StartNew();
                stop.CancelAfter(70000);
                try
                {
                    new NativeReader(adb,info.Pid,module).Stream(frame=> {
                        times.Add(clock.Elapsed.TotalMilliseconds);work.Add(frame.ReadMilliseconds);counts.Add(frame.Actors.Length);
                        if(prior!=null)
                        {
                            if(frame.CameraPosition.Distance(prior.CameraPosition)>.1f||frame.CameraRotation.Distance(prior.CameraRotation)>.01f)cameraChanges++;
                            if(frame.Origin.Distance(prior.Origin)>.1f)positionChanges++;
                        }
                        prior=frame;
                    },stop.Token).GetAwaiter().GetResult();
                }
                catch(OperationCanceledException) { }
                double[] intervals=times.Skip(1).Select((time,index)=>time-times[index]).ToArray();
                string mode=adb.Shell("getenforce",CancellationToken.None);
                var result=new {Frames=times.Count,Build=info.VersionCode,Privilege=adb.Privilege.ToString(),EnforcementAfterStop=mode,FirstFrameMilliseconds=times.Count==0?0:times[0],Hertz=times.Count<2?0:(times.Count-1)*1000/(times.Last()-times.First()),MeanIntervalMilliseconds=intervals.Length==0?0:intervals.Average(),MaxIntervalMilliseconds=intervals.Length==0?0:intervals.Max(),MeanReadMilliseconds=work.Count==0?0:work.Average(),MinEntities=counts.Count==0?0:counts.Min(),MaxEntities=counts.Count==0?0:counts.Max(),CameraChanges=cameraChanges,PositionChanges=positionChanges,CancellationCompleted=stop.IsCancellationRequested,ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds};
                File.WriteAllText(args[2],new JavaScriptSerializer().Serialize(result));
                return times.Count>500&&mode=="Enforcing"&&stop.IsCancellationRequested?0:1;
            }
            catch(Exception error) {File.WriteAllText(args[2],error.ToString());return 1;}
        }
    }
}
