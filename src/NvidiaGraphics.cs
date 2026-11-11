using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ESPWorkbench
{
    // Public NVAPI DRS ABI: NVIDIA/nvapi, nvapi.h and nvapi_interface.h.
    // Application profiles only. Never access the base/global profile.
    internal sealed class NvidiaGraphics : IDisposable
    {
        internal static readonly string[] Applications={"GameLoopEmulator.exe","GameLoopVm.exe","GLABoxHeadless.exe","AndroidEmulator.exe","AndroidEmulatorEn.exe","AndroidEmulatorEx.exe","dnplayer.exe","LdVBoxHeadless.exe"};
        private const uint Fxaa=0x1074C972;
        private const int SettingSize=12320,CurrentValueOffset=8220;
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
        [DllImport("kernel32.dll")] private static extern IntPtr GetProcAddress(IntPtr library,string name);
        [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr Query(uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NoArgs();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Create(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SessionCall(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)] private delegate int FindApp(IntPtr session,string app,out IntPtr profile,IntPtr info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProfile(IntPtr session,IntPtr info,out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileData(IntPtr session,IntPtr profile,IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ProfileCall(IntPtr session,IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetSetting(IntPtr session,IntPtr profile,uint id,IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int DeleteSetting(IntPtr session,IntPtr profile,uint id);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorMessage(int code,[Out] StringBuilder message);
        private IntPtr library,session;
        private Query query;
        private bool initialized;
        private T Function<T>(uint id) where T:class
        {
            IntPtr address=query(id);
            if(address==IntPtr.Zero)throw new InvalidOperationException(L.Text("تعريف NVIDIA لا يدعم العملية المطلوبة."));
            return Marshal.GetDelegateForFunctionPointer(address,typeof(T)) as T;
        }
        private void Check(int code)
        {
            if(code==0)return;
            StringBuilder message=new StringBuilder(64);Function<ErrorMessage>(0x6c2d048c)(code,message);
            throw new InvalidOperationException("NVIDIA: "+message+" ("+code+"). "+(code==-175?L.Text("شغّل الأداة كمسؤول."):""));
        }
        internal NvidiaGraphics()
        {
            try
            {
                library=LoadLibraryEx("nvapi64.dll",IntPtr.Zero,0x800); // System32 only; no local DLL loading.
                if(library==IntPtr.Zero)throw new InvalidOperationException(L.Text("تعريف NVIDIA غير متاح. ثبّت تعريف كرت NVIDIA."));
                IntPtr address=GetProcAddress(library,"nvapi_QueryInterface");
                if(address==IntPtr.Zero)throw new InvalidOperationException(L.Text("NVAPI غير متاح في التعريف."));
                query=(Query)Marshal.GetDelegateForFunctionPointer(address,typeof(Query));
                Check(Function<NoArgs>(0x0150e828)());initialized=true;
                Check(Function<Create>(0x0694d52e)(out session));
                Check(Function<SessionCall>(0x375dbd6b)(session));
            }
            catch { Dispose();throw; }
        }
        private sealed class Buffer : IDisposable
        {
            internal IntPtr Pointer;
            internal Buffer(int size) { Pointer=Marshal.AllocHGlobal(size);Marshal.Copy(new byte[size],0,Pointer,size);Marshal.WriteInt32(Pointer,size|0x10000); }
            internal void Text(int offset,string value) { byte[] bytes=Encoding.Unicode.GetBytes(value+"\0");Marshal.Copy(bytes,0,IntPtr.Add(Pointer,offset),bytes.Length); }
            public void Dispose() { if(Pointer!=IntPtr.Zero)Marshal.FreeHGlobal(Pointer);Pointer=IntPtr.Zero; }
        }
        private IntPtr Find(string app)
        {
            using(Buffer info=new Buffer(12296))
            {
                IntPtr profile;int code=Function<FindApp>(0xeee566b2)(session,app,out profile,info.Pointer);
                if(code==-166)return IntPtr.Zero;
                Check(code);if(profile==IntPtr.Zero)throw new InvalidOperationException(L.Text("NVIDIA returned an empty application profile."));return profile;
            }
        }
        private IntPtr Add(string app)
        {
            IntPtr profile;
            using(Buffer info=new Buffer(4116))
            {
                info.Text(4,L.ProductName+" Image Quality / "+app);
                Check(Function<CreateProfile>(0xcc176068)(session,info.Pointer,out profile));
            }
            using(Buffer info=new Buffer(12296)) { info.Text(8,app);Check(Function<ProfileData>(0x4347a9de)(session,profile,info.Pointer)); }
            return profile;
        }
        private byte[] Read(IntPtr profile)
        {
            using(Buffer setting=new Buffer(SettingSize))
            {
                int code=Function<GetSetting>(0x73bf8338)(session,profile,Fxaa,setting.Pointer);
                if(code==-160)return null;Check(code);
                byte[] bytes=new byte[SettingSize];Marshal.Copy(setting.Pointer,bytes,0,bytes.Length);return bytes;
            }
        }
        private void Write(IntPtr profile,bool enabled)
        {
            using(Buffer setting=new Buffer(SettingSize))
            {
                Marshal.WriteInt32(setting.Pointer,4100,unchecked((int)Fxaa));
                Marshal.WriteInt32(setting.Pointer,CurrentValueOffset,enabled?1:0);
                Check(Function<ProfileData>(0x577dd202)(session,profile,setting.Pointer));
            }
        }
        internal static void SetFxaa(bool enabled,Action persist=null)
        {
            using(NvidiaGraphics driver=new NvidiaGraphics())driver.Apply(enabled,persist);
        }
        private void Apply(bool enabled,Action persist)
        {
            Dictionary<IntPtr,byte[]> originals=new Dictionary<IntPtr,byte[]>();
            List<IntPtr> created=new List<IntPtr>();bool saving=false;
            try
            {
                foreach(string app in Applications)
                {
                    IntPtr profile=Find(app);
                    if(profile==IntPtr.Zero) { profile=Add(app);created.Add(profile); }
                    if(originals.ContainsKey(profile))continue;
                    originals.Add(profile,Read(profile));Write(profile,enabled);
                }
                saving=true;Check(Function<SessionCall>(0xfcbc7e14)(session));
                using(NvidiaGraphics verification=new NvidiaGraphics())
                foreach(string app in Applications)
                {
                    IntPtr profile=verification.Find(app);byte[] value=profile==IntPtr.Zero?null:verification.Read(profile);
                    if(value==null||BitConverter.ToUInt32(value,CurrentValueOffset)!=(enabled?1u:0u))throw new InvalidOperationException(L.Format("Could not verify FXAA settings for {0}",app));
                }
                if(persist!=null)persist();
            }
            catch(Exception failure)
            {
                // Unsaved edits disappear with the session. After a save attempt,
                // restore the exact user override or its previous inheritance.
                if(saving)
                {
                    try
                    {
                        foreach(KeyValuePair<IntPtr,byte[]> item in originals)
                        {
                            if(created.Contains(item.Key))continue;
                            byte[] value=item.Value;
                            if(value==null||BitConverter.ToInt32(value,4108)!=0||BitConverter.ToInt32(value,4112)!=0)
                            { int status=Function<DeleteSetting>(0xe4a26362)(session,item.Key,Fxaa);if(status!=-160)Check(status); }
                            else using(Buffer setting=new Buffer(SettingSize)) { Marshal.Copy(value,0,setting.Pointer,value.Length);Check(Function<ProfileData>(0x577dd202)(session,item.Key,setting.Pointer)); }
                        }
                        foreach(IntPtr profile in created)Check(Function<ProfileCall>(0x17093206)(session,profile));
                        Check(Function<SessionCall>(0xfcbc7e14)(session));
                    }
                    catch(Exception rollback) { throw new InvalidOperationException(L.Format("{0} Could not restore previous NVIDIA settings: {1}",failure.Message,rollback.Message),failure); }
                }
                throw;
            }
        }
        internal static string Probe()
        {
            using(NvidiaGraphics driver=new NvidiaGraphics())
            {
                List<string> lines=new List<string>();
                foreach(string app in Applications)
                {
                    IntPtr profile=driver.Find(app);byte[] value=profile==IntPtr.Zero?null:driver.Read(profile);
                    lines.Add(app+": "+(value==null?"no application override":BitConverter.ToUInt32(value,CurrentValueOffset).ToString()));
                }
                return String.Join(Environment.NewLine,lines);
            }
        }
        // Explicit diagnostic only: verify real saved settings, then restore every
        // touched override/profile. The normal self-test never changes the driver.
        internal static string TestRoundTrip()
        {
            Dictionary<string,byte[]> original=new Dictionary<string,byte[]>();
            HashSet<string> absent=new HashSet<string>();
            using(NvidiaGraphics driver=new NvidiaGraphics())
                foreach(string app in Applications) { IntPtr profile=driver.Find(app);if(profile==IntPtr.Zero)absent.Add(app);else original.Add(app,driver.Read(profile)); }
            try
            {
                SetFxaa(false);SetFxaa(true);
                string enabledState=Probe();bool rejected=false;
                try { SetFxaa(false,delegate { throw new System.IO.IOException("simulated preference save failure"); }); }
                catch(System.IO.IOException) { rejected=true; }
                if(!rejected||Probe()!=enabledState)throw new Exception("A preference-save failure did not restore the NVIDIA settings.");
                SetFxaa(false);
            }
            finally
            {
                using(NvidiaGraphics driver=new NvidiaGraphics())
                {
                    HashSet<IntPtr> restored=new HashSet<IntPtr>();
                    foreach(string app in Applications)
                    {
                        IntPtr profile=driver.Find(app);if(profile==IntPtr.Zero||!restored.Add(profile))continue;
                        if(absent.Contains(app)) { driver.Check(driver.Function<ProfileCall>(0x17093206)(driver.session,profile));continue; }
                        byte[] value=original[app];
                        if(value==null||BitConverter.ToInt32(value,4108)!=0||BitConverter.ToInt32(value,4112)!=0)
                        { int code=driver.Function<DeleteSetting>(0xe4a26362)(driver.session,profile,Fxaa);if(code!=-160)driver.Check(code); }
                        else using(Buffer setting=new Buffer(SettingSize)) { Marshal.Copy(value,0,setting.Pointer,value.Length);driver.Check(driver.Function<ProfileData>(0x577dd202)(driver.session,profile,setting.Pointer)); }
                    }
                    driver.Check(driver.Function<SessionCall>(0xfcbc7e14)(driver.session));
                }
            }
            using(NvidiaGraphics driver=new NvidiaGraphics())
                foreach(string app in Applications)
                {
                    IntPtr profile=driver.Find(app);
                    if(absent.Contains(app)) { if(profile!=IntPtr.Zero)throw new Exception("Test profile was not removed: "+app);continue; }
                    byte[] value=driver.Read(profile),previous=original[app];
                    if((value==null)!=(previous==null)||(value!=null&&(BitConverter.ToUInt32(value,CurrentValueOffset)!=BitConverter.ToUInt32(previous,CurrentValueOffset)||BitConverter.ToInt32(value,4108)!=BitConverter.ToInt32(previous,4108)||BitConverter.ToInt32(value,4112)!=BitConverter.ToInt32(previous,4112))))throw new Exception("Original setting was not restored: "+app);
                }
            return "PASS real NVIDIA FXAA Off / On / Off saved and read back for all "+Applications.Length+" emulator executables.\r\nPASS simulated preference-save failure restores the previous NVIDIA values.\r\nPASS original driver overrides and profile associations restored.\r\nThis verifies driver configuration, not game rendering or visual FXAA support.";
        }
        public void Dispose()
        {
            try { if(session!=IntPtr.Zero)Function<SessionCall>(0xdad9cff8)(session); }
            finally { session=IntPtr.Zero;try { if(initialized)Function<NoArgs>(0xd22bdd7e)(); }finally { initialized=false;if(library!=IntPtr.Zero)FreeLibrary(library);library=IntPtr.Zero; } }
        }
    }
}
