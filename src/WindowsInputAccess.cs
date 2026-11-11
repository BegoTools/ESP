using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ESPWorkbench
{
    internal static class WindowsInputAccess
    {
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll",SetLastError=true)] private static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
        [DllImport("advapi32.dll",SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr data,int length,out int needed);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] private static extern IntPtr GetSidSubAuthority(IntPtr sid,uint index);
        public static int Integrity(int pid)
        {
            IntPtr process=OpenProcess(0x1000,false,pid),token=IntPtr.Zero,buffer=IntPtr.Zero;
            try
            {
                if(process==IntPtr.Zero||!OpenProcessToken(process,8,out token))return -1;
                int size;GetTokenInformation(token,25,IntPtr.Zero,0,out size);if(size<1||size>65536)return -1;
                buffer=Marshal.AllocHGlobal(size);if(!GetTokenInformation(token,25,buffer,size,out size))return -1;
                IntPtr sid=Marshal.ReadIntPtr(buffer);byte count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));if(count<1)return -1;
                return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
            }
            finally {if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(token!=IntPtr.Zero)CloseHandle(token);if(process!=IntPtr.Zero)CloseHandle(process);}
        }
        public static readonly int CurrentIntegrity=Integrity(Process.GetCurrentProcess().Id);
        public static string BlockReason(int current,int target)
        {
            if(current<0||target<0)return L.Text("تعذر التحقق من صلاحيات الإدخال");
            return current<target?L.Text("المحاكي بصلاحيات أعلى — أعد تشغيل الأداة كمسؤول"):null;
        }
    }
}
