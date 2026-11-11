using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ESPWorkbench
{
    internal static class UserData
    {
        internal static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
#if DISTRIBUTION
            ReleaseConfig.Product,
#else
            "SpectraVision",
#endif
            "Data");

        internal static void Replace(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { File.Delete(temporary); }
        }
    }

#if DISTRIBUTION
    internal static class Distribution
    {
        internal static Version Version { get { return typeof(Distribution).Assembly.GetName().Version; } }
        internal static string VersionText { get { return Version.ToString(3); } }

        internal static T ReadJson<T>(byte[] bytes)
        {
            return new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<T>(Encoding.UTF8.GetString(bytes));
        }

        internal static HttpWebRequest Request(string path)
        {
            Uri origin = new Uri(ReleaseConfig.Origin);
            if (origin.Scheme != Uri.UriSchemeHttps && !origin.IsLoopback) throw new InvalidOperationException(L.Text("HTTPS is required."));
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(origin, path));
            request.AllowAutoRedirect = false;
            request.Timeout = 8500;
            request.ReadWriteTimeout = 8500;
            request.UserAgent = "SpectraVision/" + VersionText;
            request.Proxy = origin.IsLoopback ? null : request.Proxy;
            return request;
        }

        internal static byte[] Read(Stream stream, int limit)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (memory.Length + count > limit) throw new InvalidDataException();
                    memory.Write(buffer, 0, count);
                }
                return memory.ToArray();
            }
        }

        internal static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }
    }

    internal sealed class LicenseReply
    {
        public long expiresAt { get; set; }
        public long offlineUntil { get; set; }
        public long serverTime { get; set; }
        public string error { get; set; }
    }

    internal sealed class SavedLicense
    {
        public string Token { get; set; }
        public string Device { get; set; }
        public LicenseReply Lease { get; set; }
    }

    internal sealed class LicenseClient
    {
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly string StatePath = Path.Combine(UserData.Folder, "activation.bin");
        private readonly string device;
        private SavedLicense state;

        internal string Message { get; private set; }
        internal bool HasToken { get { return state != null; } }
        internal bool IsUsable
        {
            get
            {
                if (state == null || state.Lease == null) return false;
                long now = (long)(DateTimeOffset.UtcNow - Epoch).TotalSeconds;
                return now >= state.Lease.serverTime - 120 && now < state.Lease.offlineUntil && now < state.Lease.expiresAt;
            }
        }

        internal string Description
        {
            get
            {
                if (state == null || state.Lease == null) return L.Text("الترخيص يحتاج تفعيل.");
                return L.Format("Code expires  {0}",Epoch.AddSeconds(state.Lease.expiresAt).ToLocalTime().ToString("dd/MM/yyyy  HH:mm", L.Culture));
            }
        }

        internal LicenseClient()
        {
            string machineGuid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
            if (String.IsNullOrWhiteSpace(machineGuid)) throw new InvalidOperationException(L.Text("تعذر تحديد جهاز Windows."));
            using (SHA256 hash = SHA256.Create()) device = Distribution.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes("SpectraVision/v1/" + machineGuid.Trim().ToLowerInvariant())));
            try
            {
                byte[] data = ProtectedData.Unprotect(File.ReadAllBytes(StatePath), null, DataProtectionScope.CurrentUser);
                SavedLicense saved = Distribution.ReadJson<SavedLicense>(data);
                if (saved != null && saved.Device == device && !String.IsNullOrEmpty(saved.Token)) state = saved;
            }
            catch (IOException) { }
            catch (CryptographicException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }
            Message = L.Text("أدخل الكود اللي استلمته لتفعيل الأداة على الجهاز ده.");
        }

        internal Task<bool> Activate(string code)
        {
            byte[] bytes = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Exchange("/api/activate", Distribution.Hex(bytes), code);
        }

        internal Task<bool> Verify()
        {
            return state == null ? Task.FromResult(false) : Exchange("/api/verify", state.Token, null);
        }

        private async Task<bool> Exchange(string path, string token, string code)
        {
            LicenseReply reply;
            try { reply = await Task.Run(() => Send(path, token, code)); }
            catch (WebException)
            {
                Message = L.Text("تعذر الاتصال بسيرفر التفعيل. راجع الإنترنت وحاول تاني.");
                return code == null && IsUsable;
            }
            catch (IOException)
            {
                Message = L.Text("سيرفر التفعيل غير متاح حاليًا. حاول تاني.");
                return code == null && IsUsable;
            }
            if (reply != null && String.IsNullOrEmpty(reply.error) && reply.expiresAt > reply.serverTime &&
                reply.offlineUntil > reply.serverTime && reply.offlineUntil <= reply.expiresAt && reply.offlineUntil <= reply.serverTime + 86400 &&
                reply.expiresAt <= 253402300799L)
            {
                SavedLicense saved = new SavedLicense { Token = token, Device = device, Lease = reply };
                byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(saved));
                try { UserData.Replace(StatePath, ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)); }
                catch (IOException) { Message = L.Text("تعذر حفظ التفعيل على الجهاز."); return false; }
                catch (UnauthorizedAccessException) { Message = L.Text("تعذر حفظ التفعيل على الجهاز."); return false; }
                state = saved;
                if (IsUsable) { Message = ""; return true; }
                Message = L.Text("راجع تاريخ وساعة Windows وحاول تاني.");
                return false;
            }
            string reason = reply == null ? null : reply.error;
            if (code == null && (reason == "revoked" || reason == "device_removed"))
            {
                state = null;
                try { File.Delete(StatePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else if (code == null && reason == "expired" && state != null)
            {
                state.Lease.offlineUntil = 0;
            }
            switch (reason)
            {
                case "expired": Message = L.Text("صلاحية الكود انتهت. اطلب تمديده أو أدخل كود جديد."); break;
                case "revoked": Message = L.Text("الكود اتوقف. تواصل مع المسؤول أو أدخل كود جديد."); break;
                case "device_removed": Message = L.Text("الجهاز اتفك ربطه. فعّل الأداة بكود صالح."); break;
                case "device_limit": Message = L.Text("الكود وصل للحد الأقصى من الأجهزة."); break;
                case "invalid_code": case "bad_input": Message = L.Text("الكود غير صحيح. راجع الحروف والأرقام."); break;
                default: Message = L.Text("سيرفر التفعيل غير متاح حاليًا. حاول تاني."); break;
            }
            return false;
        }

        private LicenseReply Send(string path, string token, string code)
        {
            var body = new Dictionary<string, string> { { "device", device }, { "token", token } };
            if (code != null) body.Add("code", code);
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            HttpWebRequest request = Distribution.Request(path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length;
            using (Stream output = request.GetRequestStream()) output.Write(bytes, 0, bytes.Length);
            HttpWebResponse response;
            try { response = (HttpWebResponse)request.GetResponse(); }
            catch (WebException ex)
            {
                response = ex.Response as HttpWebResponse;
                if (response == null) throw;
                int status = (int)response.StatusCode;
                if (status != 400 && status != 403) { response.Dispose(); throw; }
            }
            using (response)
            using (Stream input = response.GetResponseStream())
            {
                try { return Distribution.ReadJson<LicenseReply>(Distribution.Read(input, 8192)); }
                catch (ArgumentException) { throw new IOException(L.Text("Invalid license response.")); }
                catch (InvalidOperationException) { throw new IOException(L.Text("Invalid license response.")); }
            }
        }
    }

    internal sealed class ActivationForm : Form
    {
        private readonly TextBox code = new TextBox();
        private readonly Button activate = new Button();
        private readonly Label status = new Label();
        private readonly LicenseClient client;
        private bool working;

        internal ActivationForm(LicenseClient client, bool verifyExisting)
        {
            this.client = client;
            Text = L.Text("RAM HAX | Activation");
            ClientSize = new Size(490, 245);
            AutoScaleMode = AutoScaleMode.Font;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = SpectraTheme.Background;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 11);
            Label title = new Label { Text = L.Text("أدخل كود التفعيل"), Bounds = new Rectangle(24, 20, 440, 39), RightToLeft=L.Direction, TextAlign = L.Rtl?ContentAlignment.MiddleRight:ContentAlignment.MiddleLeft, ForeColor = SpectraTheme.Accent, Font = new Font(Font, FontStyle.Bold) };
            status.Bounds = new Rectangle(24, 62, 440, 58);
            status.RightToLeft = L.Direction;
            status.Text = client.Message;
            code.Bounds = new Rectangle(24, 123, 440, 32);
            code.MaxLength = 64;
            code.AccessibleName = L.Text("كود التفعيل");
            activate.Text = L.Text("تفعيل");
            activate.Bounds = new Rectangle(333, 173, 131, 43);
            activate.BackColor = SpectraTheme.Surface;
            activate.FlatStyle = FlatStyle.Flat;
            activate.Click += async delegate { if (code.Text.Trim().Length != 0) await Check(false); };
            Button exit = new Button { Text = L.Text("خروج"), Bounds = new Rectangle(24, 173, 100, 43), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat };
            Controls.AddRange(new Control[] { title, status, code, activate, exit });
            AcceptButton = activate;
            CancelButton = exit;
            if (verifyExisting) Shown += async delegate { await Check(true); };
        }

        private async Task Check(bool existing)
        {
            if (working) return;
            working = true;
            code.Enabled = activate.Enabled = false;
            status.Text = L.Text("جاري التحقق من الترخيص…");
            try
            {
                bool accepted = existing ? await client.Verify() : await client.Activate(code.Text.Trim());
                if (IsDisposed) return;
                if (accepted) DialogResult = DialogResult.OK;
                else status.Text = client.Message;
            }
            finally
            {
                working = false;
                if (!IsDisposed && DialogResult == DialogResult.None) { code.Enabled = activate.Enabled = true; code.Focus(); }
            }
        }
    }
#endif
}
