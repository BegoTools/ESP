using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class SupportLink
    {
        internal const string Address="https://t.me/ramhaxsuppoort_bot";

        internal static Button CreateButton()
        {
            var button=new SpectraButton { Name="TelegramSupport",Text=L.Text("Telegram support"),Highlighted=true };
            button.Click+=delegate {
                try
                {
                    using(Process process=Process.Start(new ProcessStartInfo(Address) { UseShellExecute=true })) { }
                }
                catch(Win32Exception) { ShowAddress(button); }
                catch(InvalidOperationException) { ShowAddress(button); }
            };
            return button;
        }

        private static void ShowAddress(Control button)
        {
            MessageBox.Show(button.FindForm(),L.Text("Open the Telegram support link manually.")+Environment.NewLine+Address,
                L.Text("Telegram support"),MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
    }
}
