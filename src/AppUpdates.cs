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

    internal sealed class ReleaseMetadata
    {
        public string channel { get; set; }
        public string currentVersion { get; set; }
        public string manifestUrl { get; set; }
        public string checksum { get; set; }
    }

    internal sealed class AppRelease
    {
        public string version { get; set; }
        public string sha256 { get; set; }
        public long size { get; set; }
        public string notes { get; set; }
        public string url { get; set; }
        public string channel { get; set; }
        public string minSupportedVersion { get; set; }
        private Uri installer;
        private const long MaximumSize = 2L * 1024 * 1024 * 1024;
        private const string ReleaseBase = "https://github.com/BegoTools/ESP/releases/download/";

        internal static AppRelease Check()
        {
            HttpWebRequest request = Distribution.Request("/v2/client/version");
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.BypassCache);
            ReleaseMetadata metadata;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
                metadata = Distribution.ReadJson<ReleaseMetadata>(Distribution.Read(stream, 8192));
            if (metadata == null || metadata.channel != ReleaseConfig.Channel) throw new InvalidDataException();
            if (metadata.currentVersion == null && metadata.manifestUrl == null) return null;
            Version latest = ParseVersion(metadata.currentVersion);
            if (latest <= new Version(Distribution.VersionText)) return null;
            string location = ReleaseBase + "sv-" + ReleaseConfig.Channel + "-v" + metadata.currentVersion + "/";
            if (metadata.manifestUrl != location + "latest.json") throw new InvalidDataException();
            using (var response = GetRelease(new Uri(metadata.manifestUrl)))
            using (Stream stream = response.GetResponseStream())
                return ReadSignedManifest(stream, metadata);
        }

        internal static AppRelease ReadSignedManifest(Stream stream, ReleaseMetadata metadata)
        {
            Version latest = ParseVersion(metadata.currentVersion);
            if (latest <= new Version(Distribution.VersionText)) return null;
            string location = ReleaseBase + "sv-" + ReleaseConfig.Channel + "-v" + metadata.currentVersion + "/";
            var envelope = Distribution.ReadJson<ReleaseEnvelope>(Distribution.Read(stream, 16384));
            if (envelope == null) throw new InvalidDataException(L.Text("Invalid release information."));
            byte[] payload = Convert.FromBase64String(envelope.payload);
            byte[] signature = Convert.FromBase64String(envelope.signature);
            if (!VerifySignature(payload, signature, ReleaseConfig.Modulus, ReleaseConfig.Exponent) &&
                !VerifySignature(payload, signature, ReleaseConfig.LegacyModulus, ReleaseConfig.LegacyExponent))
                throw new CryptographicException(L.Text("Invalid release signature."));
            var release = Distribution.ReadJson<AppRelease>(payload);
            if (release == null || release.version != metadata.currentVersion || release.channel != ReleaseConfig.Channel ||
                ParseVersion(release.minSupportedVersion) > latest || release.size < 1 || release.size > MaximumSize ||
                release.sha256 == null || release.sha256 != metadata.checksum ||
                !System.Text.RegularExpressions.Regex.IsMatch(release.sha256, "\\A[0-9a-f]{64}\\z") ||
                release.url != location + "SpectraVision-Setup-" + release.version + ".exe")
                throw new InvalidDataException(L.Text("Invalid release information."));
            release.installer = new Uri(release.url);
            return release;
        }

        private static Version ParseVersion(string value)
        {
            Version version;
            if (!Version.TryParse(value, out version) || version.Build < 0 || version.Revision != -1 ||
                version.ToString(3) != value || version.Major > 65534 || version.Minor > 65534 || version.Build > 65534)
                throw new InvalidDataException(L.Text("Invalid release information."));
            return version;
        }

        private static bool VerifySignature(byte[] payload, byte[] signature, string modulus, string exponent)
        {
            if (String.IsNullOrEmpty(modulus)) return false;
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.ImportParameters(new RSAParameters { Modulus = Convert.FromBase64String(modulus), Exponent = Convert.FromBase64String(exponent) });
                return rsa.VerifyData(payload, "SHA256", signature);
            }
        }

        private static HttpWebResponse GetRelease(Uri uri)
        {
            for (int redirects = 0; redirects <= 2; redirects++)
            {
                if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo) ||
                    (uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com"))
                    throw new InvalidDataException(L.Text("Invalid release information."));
                HttpWebResponse response = (HttpWebResponse)Distribution.Request(uri).GetResponse();
                if (response.StatusCode == HttpStatusCode.OK) return response;
                using (response)
                {
                    Uri target;
                    if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 ||
                        !Uri.TryCreate(uri, response.Headers["Location"], out target))
                        throw new IOException(L.Text("Invalid update response."));
                    uri = target;
                }
            }
            throw new IOException(L.Text("Invalid update response."));
        }

        internal string Download()
        {
            string folder = Path.Combine(UserData.Folder, "Updates");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "SpectraVision-Setup-" + version + "-" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                using (var response = GetRelease(installer))
                using (Stream input = response.GetResponseStream())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (response.StatusCode != HttpStatusCode.OK || (response.ContentLength >= 0 && response.ContentLength != size))
                        throw new IOException(L.Text("Incomplete update."));
                    byte[] buffer = new byte[65536];
                    int count;
                    long total = 0;
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

        internal FileStream VerifyPackage(string path)
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
        private LicenseService license;
        private AppRelease availableRelease;
        private string stagedSetup;
        private bool updating, checkingRelease;
        private Button updateButton;
        private Label licenseLabel;
        private Button activationButton;
        private System.Threading.CancellationTokenSource demoPermission;
        private bool licenseSuspended;

        internal void ConnectDistribution(LicenseService client)
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
            activationButton = new Button { Width = 145, Height = 38 };
            ConfigureButton(activationButton, "تفعيل", delegate
            {
                using (var activation = new ActivationForm(license, false)) activation.ShowDialog(this);
            });
            actions.Controls.Add(licenseLabel);
            actions.Controls.Add(activationButton);
            actions.Controls.Add(updateButton);
            client.Changed += LicenseChanged;
            FormClosed += delegate { client.Changed -= LicenseChanged; };
            Shown += async delegate { ApplyLicenseState(); await CheckRelease(false); };
        }

        private void LicenseChanged()
        {
            if (!IsHandleCreated || IsDisposed) return;
            try { BeginInvoke(new Action(ApplyLicenseState)); }
            catch (InvalidOperationException) { }
        }

        private void ApplyLicenseState()
        {
            if (closing) return;
            bool authorized = license.Allows(null);
            SuspendUnlicensedFeatures();
            if (!authorized && !licenseSuspended) ReleaseShortcuts();
            if (authorized && licenseSuspended && !shortcutsSuspended) RebindShortcuts();
            licenseSuspended = !authorized;
            licenseLabel.Text = license.Description;
            activationButton.Visible = !authorized;
            SetBusy(busy);
        }

        private void SuspendUnlicensedFeatures()
        {
            if ((liveDisplay && !license.Allows("live")) ||
                (demoMode && (!license.Allows("demo") || demoPermission == null || demoPermission.IsCancellationRequested)) ||
                (liveDisplay && readerCancel != null && readerCancel.IsCancellationRequested))
            {
                if (readerCancel != null) readerCancel.Cancel();
                if (demoPermission != null) { demoPermission.Dispose(); demoPermission = null; }
                liveDisplay = demoMode = false;
                latest = null;
                frameBuffer.Clear();
                overlay.Suppressed = true;
                overlay.Hide();
                StopAimInput();
                SetStatus("Stopped");
            }
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
