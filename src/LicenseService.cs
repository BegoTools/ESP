#if DISTRIBUTION
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ESPWorkbench
{
    internal sealed class LicenseReply
    {
        public bool authorized { get; set; }
        public long expiresAt { get; set; }
        public long leaseExpiresAt { get; set; }
        public long serverTime { get; set; }
        public string error { get; set; }
        public string token { get; set; }
        public Dictionary<string, bool> permissions { get; set; }
    }

    internal sealed class SavedLicense
    {
        public int SchemaVersion { get; set; }
        public string Device { get; set; }
        public string Token { get; set; }
        public Dictionary<string, object> Lease { get; set; }
        public string V2Token { get; set; }
    }

    internal sealed class LicenseService : IDisposable
    {
        private const int HeartbeatSeconds = 120;
        private const int RetrySeconds = 15;
        private const int LeaseSeconds = 128;
        private static readonly string StatePath = Path.Combine(UserData.Folder, "activation.bin");
        private static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private static readonly Regex TokenPattern = new Regex(@"\A[a-f0-9]{64}\z");
        private readonly object gate = new object();
        private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Timer timer;
        private readonly string device;
        private CancellationTokenSource work = new CancellationTokenSource();
        private SavedLicense saved;
        private LicenseReply lease;
        private LicenseState state = LicenseState.NotActivated;
        private string message = "أدخل الكود اللي استلمته لتفعيل الأداة على الجهاز ده.";
        private double validUntil, checkAt, lastTick;
        private DateTime lastWallClock = DateTime.UtcNow;
        private int generation;
        private bool disposed;
        private bool suspended;

        internal event Action Changed;
        internal bool HasToken { get { lock (gate) return saved != null; } }
        internal LicenseState State { get { lock (gate) return state; } }
        internal string Message { get { lock (gate) return L.Text(message); } }

        internal string Description
        {
            get
            {
                lock (gate)
                {
                    if (!Active) return L.Text(message);
                    return L.Format("Code expires  {0}", Epoch.AddSeconds(lease.expiresAt).ToLocalTime().ToString("dd/MM/yyyy  HH:mm", L.Culture));
                }
            }
        }

        private bool Active
        {
            get { return !disposed && !suspended && state == LicenseState.Active && clock.Elapsed.TotalSeconds < validUntil; }
        }

        internal bool Allows(string feature)
        {
            lock (gate)
            {
                bool allowed;
                return Active && (feature == null || lease.permissions.TryGetValue(feature, out allowed) && allowed);
            }
        }

        internal CancellationTokenSource BeginWork(string feature)
        {
            lock (gate)
            {
                return Allows(feature) ? CancellationTokenSource.CreateLinkedTokenSource(work.Token) : null;
            }
        }

        internal LicenseService()
        {
            string guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
            if (String.IsNullOrWhiteSpace(guid)) throw new InvalidOperationException(L.Text("تعذر تحديد جهاز Windows."));
            using (SHA256 hash = SHA256.Create())
                device = Distribution.Hex(hash.ComputeHash(Encoding.UTF8.GetBytes("SpectraVision/v1/" + guid.Trim().ToLowerInvariant())));
            try
            {
                byte[] bytes = ProtectedData.Unprotect(File.ReadAllBytes(StatePath), null, DataProtectionScope.CurrentUser);
                SavedLicense cached = Distribution.ReadJson<SavedLicense>(bytes);
                if (cached != null && cached.Device == device && cached.SchemaVersion <= 2 &&
                    (IsToken(cached.V2Token) || IsToken(cached.Token))) saved = cached;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (CryptographicException) { }
            catch (InvalidOperationException) { }
            catch (ArgumentException) { }

            checkAt = Double.PositiveInfinity;
            NetworkChange.NetworkAddressChanged += NetworkChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.TimeChanged += TimeChanged;
            timer = new Timer(Tick, null, 1000, 1000);
        }

        private static bool IsToken(string token)
        {
            return token != null && TokenPattern.IsMatch(token);
        }

        private void Notify()
        {
            Action changed = Changed;
            if (changed != null) changed();
        }

        private void CancelWork()
        {
            work.Cancel();
            work.Dispose();
            work = new CancellationTokenSource();
        }

        private void SetState(LicenseState next, string reason)
        {
            if (next != LicenseState.Active && state == LicenseState.Active) CancelWork();
            state = next;
            message = reason;
        }

        private void Invalidate()
        {
            lock (gate)
            {
                if (disposed) return;
                generation++;
                SetState(LicenseState.NetworkLost, "Connection changed. Checking the license again.");
                checkAt = 0;
            }
            Notify();
        }

        private void NetworkChanged(object sender, EventArgs e) { Invalidate(); }
        private void TimeChanged(object sender, EventArgs e) { Invalidate(); }

        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.StatusChange) return;
            lock (gate) suspended = e.Mode == PowerModes.Suspend;
            Invalidate();
        }

        private async void Tick(object ignored)
        {
            bool notify = false, verify;
            lock (gate)
            {
                if (disposed || suspended) return;
                double now = clock.Elapsed.TotalSeconds;
                DateTime wall = DateTime.UtcNow;
                bool jumped = Math.Abs((wall - lastWallClock).TotalSeconds - (now - lastTick)) > 5;
                lastTick = now;
                lastWallClock = wall;
                if (jumped || state == LicenseState.Active && now >= validUntil)
                {
                    generation++;
                    SetState(LicenseState.NetworkLost, "Connection changed. Checking the license again.");
                    checkAt = 0;
                    notify = true;
                }
                verify = saved != null && now >= checkAt;
            }
            if (notify) Notify();
            if (verify) await Verify().ConfigureAwait(false);
        }

        internal Task<bool> Activate(string code) { return Exchange(code); }
        internal Task<bool> Verify() { return Exchange(null); }

        private async Task<bool> Exchange(string code)
        {
            if (!await requestGate.WaitAsync(0).ConfigureAwait(false)) return false;
            try
            {
                SavedLicense cached;
                CancellationToken shutdown;
                int attempt;
                double startedAt;
                lock (gate)
                {
                    if (disposed || suspended || code == null && saved == null) return false;
                    cached = saved;
                    shutdown = lifetime.Token;
                    attempt = generation;
                    startedAt = clock.Elapsed.TotalSeconds;
                    checkAt = startedAt + RetrySeconds;
                    if (!Active || code != null) SetState(LicenseState.Checking, "جاري التحقق من الترخيص…");
                }
                Notify();
                bool migration = code == null && !IsToken(cached.V2Token);
                bool activate = code != null || migration;
                var body = new Dictionary<string, string> { { "device", device }, { "version", Distribution.VersionText } };
                if (code != null) body.Add("code", code);
                else if (migration) body.Add("legacyToken", cached.Token);
                else body.Add("token", cached.V2Token);

                LicenseReply reply;
                try
                {
                    using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown))
                    {
                        deadline.CancelAfter(8000);
                        reply = await Send(activate ? "/v2/client/activate" : "/v2/client/heartbeat", body, deadline.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception error)
                {
                    if (!(error is WebException || error is IOException || error is ArgumentException ||
                        error is InvalidOperationException || error is OperationCanceledException ||
                        error is FormatException || error is OverflowException || error is InvalidCastException)) throw;
                    reply = null;
                }

                bool accepted;
                lock (gate)
                {
                    if (disposed || suspended || generation != attempt) return false;
                    accepted = ValidReply(reply, activate) &&
                        startedAt + reply.leaseExpiresAt - reply.serverTime > clock.Elapsed.TotalSeconds;
                    if (accepted && activate)
                    {
                        var next = new SavedLicense {
                            SchemaVersion = 2, Device = device, V2Token = reply.token,
                            Token = migration ? cached.Token : null, Lease = migration ? cached.Lease : null
                        };
                        try
                        {
                            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(next));
                            UserData.Replace(StatePath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
                            saved = next;
                        }
                        catch (Exception error)
                        {
                            if (!(error is IOException || error is UnauthorizedAccessException || error is CryptographicException)) throw;
                            SetState(LicenseState.NetworkLost, "تعذر حفظ التفعيل على الجهاز.");
                            accepted = false;
                            reply = new LicenseReply { error = "save_failed" };
                        }
                    }
                    if (accepted)
                    {
                        if (lease != null)
                        {
                            foreach (var permission in lease.permissions)
                                if (permission.Value && !reply.permissions[permission.Key]) { CancelWork(); break; }
                        }
                        lease = reply;
                        validUntil = startedAt + reply.leaseExpiresAt - reply.serverTime;
                        checkAt = startedAt + HeartbeatSeconds;
                        SetState(LicenseState.Active, "");
                    }
                    else Reject(reply == null ? null : reply.error, code != null);
                }
                Notify();
                return accepted;
            }
            finally { requestGate.Release(); }
        }

        private static bool ValidReply(LicenseReply reply, bool activate)
        {
            return reply != null && reply.authorized && String.IsNullOrEmpty(reply.error) &&
                reply.serverTime > 0 && reply.serverTime <= 253402300799L && reply.expiresAt <= 253402300799L &&
                reply.leaseExpiresAt > reply.serverTime && reply.leaseExpiresAt <= reply.expiresAt &&
                reply.leaseExpiresAt - reply.serverTime <= LeaseSeconds && (!activate || IsToken(reply.token)) &&
                reply.permissions != null && reply.permissions.Count == 3 &&
                reply.permissions.ContainsKey("live") && reply.permissions.ContainsKey("demo") && reply.permissions.ContainsKey("inspection");
        }

        private void Reject(string error, bool enteringCode)
        {
            switch (error)
            {
                case "expired": SetState(LicenseState.Expired, "صلاحية الكود انتهت. اطلب تمديده أو أدخل كود جديد."); break;
                case "revoked": SetState(LicenseState.Revoked, "الكود اتوقف. تواصل مع المسؤول أو أدخل كود جديد."); break;
                case "device_removed": SetState(LicenseState.DeviceRemoved, "الجهاز اتفك ربطه. فعّل الأداة بكود صالح."); break;
                case "device_limit": SetState(LicenseState.NotActivated, "الكود وصل للحد الأقصى من الأجهزة."); break;
                case "invalid_code": SetState(LicenseState.NotActivated, "الكود غير صحيح. راجع الحروف والأرقام."); break;
                case "update_required": SetState(LicenseState.UpdateRequired, "Update required to use SpectraVision."); break;
                case "maintenance": SetState(LicenseState.Maintenance, "License service is in maintenance."); break;
                case "save_failed": break;
                default: SetState(LicenseState.NetworkLost, "تعذر الاتصال بسيرفر التفعيل. راجع الإنترنت وحاول تاني."); break;
            }
            if (enteringCode || state == LicenseState.Revoked || state == LicenseState.DeviceRemoved ||
                state == LicenseState.Expired || state == LicenseState.NotActivated) checkAt = Double.PositiveInfinity;
        }

        private static async Task<LicenseReply> Send(string path, Dictionary<string, string> body, CancellationToken token)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(body));
            HttpWebRequest request = Distribution.Request(path);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.ContentLength = bytes.Length;
            using (token.Register(request.Abort))
            {
                token.ThrowIfCancellationRequested();
                using (Stream output = await request.GetRequestStreamAsync().ConfigureAwait(false))
                    await output.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                HttpWebResponse response;
                try { response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false); }
                catch (WebException ex)
                {
                    response = ex.Response as HttpWebResponse;
                    if (response == null) throw;
                }
                using (response)
                using (Stream stream = response.GetResponseStream())
                using (var data = new MemoryStream())
                {
                    byte[] buffer = new byte[2048];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        if (data.Length + read > 8192) throw new InvalidDataException();
                        data.Write(buffer, 0, read);
                    }
                    token.ThrowIfCancellationRequested();
                    var reply = Distribution.ReadJson<LicenseReply>(data.ToArray());
                    if (response.StatusCode == HttpStatusCode.OK) return reply;
                    return new LicenseReply { error = reply == null ? null : reply.error };
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                work.Cancel();
                work.Dispose();
                lifetime.Cancel();
            }
            timer.Dispose();
            NetworkChange.NetworkAddressChanged -= NetworkChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.TimeChanged -= TimeChanged;
            lifetime.Dispose();
        }
    }
}
#endif
