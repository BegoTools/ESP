using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
class IntegrityProbe {
 [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
 [DllImport("advapi32.dll",SetLastError=true)] static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr data,int length,out int needed);
 [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
 [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
 static string Read(int pid) {
  IntPtr process=OpenProcess(0x1000,false,pid),token=IntPtr.Zero,buffer=IntPtr.Zero;
  try {
   if(process==IntPtr.Zero||!OpenProcessToken(process,8,out token))return "unavailable error="+Marshal.GetLastWin32Error();
   int n;GetTokenInformation(token,25,IntPtr.Zero,0,out n);buffer=Marshal.AllocHGlobal(n);
   if(!GetTokenInformation(token,25,buffer,n,out n))return "unavailable error="+Marshal.GetLastWin32Error();
   IntPtr sid=Marshal.ReadIntPtr(buffer);byte count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));int rid=Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
   return "integrity=0x"+rid.ToString("X");
  } finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(token!=IntPtr.Zero)CloseHandle(token);if(process!=IntPtr.Zero)CloseHandle(process);}
 }
 static void Main() {
  Console.WriteLine("Probe "+Read(Process.GetCurrentProcess().Id));
  foreach(Process p in Process.GetProcessesByName("GameLoopEmulator"))using(p)Console.WriteLine("GameLoop "+p.Id+" "+Read(p.Id));
 }
}
