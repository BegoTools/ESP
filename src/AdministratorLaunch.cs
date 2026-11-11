using System.Diagnostics;

namespace ESPWorkbench
{
    internal static class AdministratorLaunch
    {
        public static bool NeedsElevation(int integrity) { return integrity<0x3000; }
        public static ProcessStartInfo StartInfo(string executable,EmulatorKind emulator,string directory)
        {
            return new ProcessStartInfo(executable,"--emulator "+emulator+" --live --restart-elevated") { UseShellExecute=true,Verb="runas",WorkingDirectory=directory };
        }
    }
}
