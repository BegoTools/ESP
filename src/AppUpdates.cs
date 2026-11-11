#if DISTRIBUTION
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal sealed class ReleaseEnvelope
    {
        public string payload { get; set; }
        public string signature { get; set; }
    }

    internal sealed class AppRelease
    {
        public string version { get; set; }
        public string sha256 { get; set; }
        public int size { get; set; }
        public string notes { get; set; }
        private const int MaximumSize = 25 * 1024 * 1024;

        internal static AppRelease Check()
        {
            HttpWebRequest request = Distribution.Request("/updates/latest.json");
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            {
                if (response.StatusCode != HttpStatusCode.OK) throw new IOException(L.Text("Invalid update response."));
                var envelope = Distribution.ReadJson<ReleaseEnvelope>(Distribution.Read(stream, 16384));
                byte[] payload = Convert.FromBase64String(envelope.payload);
                byte[] signature = Convert.FromBase64String(envelope.signature);
                using (var rsa = new RSACryptoServiceProvider())
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(ReleaseConfig.Modulus), Exponent = Convert.FromBase64String(ReleaseConfig.Exponent) });
                    if (!rsa.VerifyData(payload, "SHA256", signature)) throw new CryptographicException(L.Text("Invalid release signature."));
                }
                var release = Distribution.ReadJson<AppRelease>(payload);
                Version latest;
                if (!Version.TryParse(release.version, out latest) || latest.Build < 0 || latest.Revision != -1 ||
                    latest.ToString(3) != release.version || release.size < 1 || release.size > MaximumSize ||
                    release.sha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(release.sha256, "\\A[0-9a-f]{64}\\z"))
                    throw new InvalidDataException(L.Text("Invalid release information."));
                return latest > new Version(Distribution.VersionText) ? release : null;
            }
        }

        internal string Download()
        {
            string folder = Path.Combine(UserData.Folder, "Updates");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "SpectraVision-Setup-" + version + "-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                HttpWebRequest request = Distribution.Request("/updates/SpectraVision-Setup-" + version + ".exe");
                using (var response = (HttpWebResponse)request.GetResponse())
                using (Stream input = response.GetResponseStream())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (response.StatusCode != HttpStatusCode.OK || (response.ContentLength >= 0 && response.ContentLength != size))
                        throw new IOException(L.Text("Incomplete update."));
                    byte[] buffer = new byte[65536];
                    int count, total = 0;
                    while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += count;
                        if (total > size) throw new IOException(L.Text("Invalid update size."));
                        output.Write(buffer, 0, count);
                    }
                }
                using (VerifyPackage(path)) { }
                return path;
            }
            catch { File.Delete(path); throw; }
        }

        private FileStream VerifyPackage(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (SHA256 sha = SHA256.Create())
                    if (stream.Length != size || Distribution.Hex(sha.ComputeHash(stream)) != sha256)
                        throw new CryptographicException(L.Text("Invalid update checksum."));
                return stream;
            }
            catch { stream.Dispose(); throw; }
        }

        internal void Install(string path)
        {
            using (VerifyPackage(path))
            using (var process = Process.Start(new ProcessStartInfo(path, "/S /UPDATE") { UseShellExecute = false }))
                if (process == null) throw new IOException(L.Text("Could not start the update."));
        }
    }

    public sealed partial class MainForm
    {
        private LicenseClient license;
        private AppRelease availableRelease;
        private string stagedSetup;
        private bool updating, checkingRelease, verifyingLicense;
        private Button updateButton;
        private Label licenseLabel;
        private DateTime nextLicenseCheck;
        internal bool LicenseEnded { get; private set; }

        internal void ConnectDistribution(LicenseClient client)
        {
            license = client;
            var actions = (FlowLayoutPanel)demo.Parent;
            licenseLabel = new Label { Text = client.Description, Width = 225, Height = 38, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White };
            updateButton = new Button();
            ConfigureButton(updateButton, "فحص التحديثات", async delegate
            {
                if (availableRelease != null) await DownloadUpdate();
                else await CheckRelease(true);
            });
            updateButton.Width = 145;
            updateButton.Height = 38;
            actions.Controls.Add(licenseLabel);
            actions.Controls.Add(updateButton);
            var leaseTimer = new System.Windows.Forms.Timer { Interval = 15000 };
            nextLicenseCheck = DateTime.UtcNow.AddMinutes(30);
            leaseTimer.Tick += async delegate
            {
                if (closing || verifyingLicense) return;
                if (DateTime.UtcNow < nextLicenseCheck && license.IsUsable) return;
                verifyingLicense = true;
                nextLicenseCheck = DateTime.UtcNow.AddMinutes(30);
                try
                {
                    bool accepted = await license.Verify();
                    if (closing) return;
                    if (!accepted || !license.IsUsable) { LicenseEnded = true; Close(); return; }
                    licenseLabel.Text = license.Description;
                }
                finally { verifyingLicense = false; }
            };
            FormClosed += delegate { leaseTimer.Dispose(); };
            Shown += async delegate { leaseTimer.Start(); await CheckRelease(false); };
        }

        private void RefreshDistributionLanguage()
        {
            if (licenseLabel != null) licenseLabel.Text=license.Description;
            if (updateButton != null) updateButton.Text=L.Text(updating?"جاري التنزيل…":availableRelease==null?"فحص التحديثات":"تحديث الآن");
            if (availableRelease != null) shellNavigation[4].Text="⚙   "+L.Text("Settings")+"  ●";
        }

        private async Task CheckRelease(bool showStatus)
        {
            if (checkingRelease || updating) return;
            checkingRelease = true;
            updateButton.Enabled = false;
            try
            {
                availableRelease = await Task.Run(() => AppRelease.Check());
                if (closing) return;
                if (availableRelease == null)
                {
                    if (showStatus) MessageBox.Show(this, L.Format("Version {0} is already up to date.",Distribution.VersionText), L.Text("Updates"));
                    return;
                }
                updateButton.Text = L.Text("تحديث الآن");
                shellNavigation[4].Text = "⚙   "+L.Text("Settings")+"  ●";
                if (!showStatus && !liveDisplay && !busy)
                    MessageBox.Show(this, L.Format("Version {0} is available. Open Settings and select Update now.",availableRelease.version), L.Text("Update available"));
            }
            catch (Exception)
            {
                if (showStatus && !closing) MessageBox.Show(this, L.Text("تعذر التحقق من التحديث. حاول تاني لاحقًا."), L.Text("Updates"));
            }
            finally
            {
                checkingRelease = false;
                if (!closing) updateButton.Enabled = true;
            }
        }

        private async Task DownloadUpdate()
        {
            if (updating || closing) return;
            if (AdministratorLaunch.NeedsElevation(WindowsInputAccess.CurrentIntegrity) == false)
            {
                MessageBox.Show(this, L.Text("افتح الأداة بشكل عادي من قائمة Start لتثبيت التحديث."), L.Text("Updates"));
                return;
            }
            AppRelease release = availableRelease;
            if (MessageBox.Show(this, L.Format("Install version {0}?\n\n{1}",release.version,release.notes),
                L.Text("RAM HAX update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            updating = true;
            updateButton.Enabled = false;
            updateButton.Text = L.Text("جاري التنزيل…");
            try
            {
                string path = await Task.Run(() => release.Download());
                if (closing) { File.Delete(path); return; }
                if ((liveDisplay || busy) && MessageBox.Show(this, L.Text("التحديث جاهز. إنهاء الجلسة وتثبيت التحديث؟"),
                    L.Text("RAM HAX update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    File.Delete(path);
                    return;
                }
                stagedSetup = path;
                Close();
            }
            catch (Exception)
            {
                if (!closing) MessageBox.Show(this, L.Text("تعذر تنزيل التحديث أو التحقق من الملف. حاول تاني."), L.Text("Updates"));
            }
            finally
            {
                updating = false;
                if (!closing) { updateButton.Text = L.Text("تحديث الآن"); updateButton.Enabled = true; }
            }
        }

        internal void InstallStagedUpdate()
        {
            if (stagedSetup != null) availableRelease.Install(stagedSetup);
        }
    }
}
#endif
