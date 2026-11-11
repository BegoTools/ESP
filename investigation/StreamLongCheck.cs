using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using ESPWorkbench;
internal static class StreamCheck
{
    private static int Main()
    {
        CancellationTokenSource stop=new CancellationTokenSource();
        try
        {
            Adb adb=new Adb(@"D:\LDPlayer\LDPlayer14\adb.exe","emulator-5554");
            GameInfo info=GameInfo.Inspect(adb,"com.tencent.ig",true,CancellationToken.None);
            ulong baseAddress=MapRegion.ModuleBase(MapRegion.Parse(info.Maps),"libUE4.so");
            List<double> arrivals=new List<double>(),reads=new List<double>(); List<int> counts=new List<int>();
            Stopwatch clock=Stopwatch.StartNew();
            NativeReader reader=new NativeReader(adb,info.Pid,baseAddress);
            stop.CancelAfter(80000);
            try { reader.Stream(frame=> { arrivals.Add(clock.Elapsed.TotalMilliseconds); reads.Add(frame.ReadMilliseconds); counts.Add(frame.Actors.Length); if (arrivals.Count>=4200) stop.Cancel(); },stop.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            double[] intervals=arrivals.Skip(1).Select((t,i)=>t-arrivals[i]).ToArray();
            object result=new { Frames=arrivals.Count,FirstFrameMilliseconds=arrivals.Count==0?0:arrivals[0],MeanIntervalMilliseconds=intervals.Length==0?0:intervals.Average(),MaxIntervalMilliseconds=intervals.Length==0?0:intervals.Max(),MeanNativeReadMilliseconds=reads.Count==0?0:reads.Average(),MinCharacters=counts.Count==0?0:counts.Min(),MaxCharacters=counts.Count==0?0:counts.Max(),CancellationCompleted=true,ElapsedMilliseconds=clock.Elapsed.TotalMilliseconds };
            File.WriteAllText(@"C:\Users\ahmed\Desktop\ESP\investigation\host-stream-60hz-70seconds.json",new JavaScriptSerializer().Serialize(result));
            return arrivals.Count==4200?0:1;
        }
        catch(Exception ex) { File.WriteAllText(@"C:\Users\ahmed\Desktop\ESP\investigation\host-stream-60hz-70seconds.json",ex.ToString()); return 1; }
        finally { stop.Dispose(); }
    }
}
