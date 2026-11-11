using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ESPWorkbench
{
    internal static class LicenseChecks
    {
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            Console.WriteLine("PASS " + message);
        }

        private static void Mode(string mode)
        {
            var request = Distribution.Request("/fixture/" + mode);
            using (request.GetResponse()) { }
        }

        private static void Invalidate(LicenseService service, string method, EventArgs args)
        {
            typeof(LicenseService).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(service, new object[] { null, args });
        }

        private static void Reject<T>(Action test, string message) where T : Exception
        {
            try { test(); }
            catch (T) { Console.WriteLine("PASS " + message); return; }
            throw new Exception(message);
        }

        private static void CheckUpdates()
        {
            var serializer = new JavaScriptSerializer();
            byte[] setup = Encoding.UTF8.GetBytes("Synthetic installer contents");
            string checksum;
            using (var sha = SHA256.Create()) checksum = Distribution.Hex(sha.ComputeHash(setup));
            const string prefix = "https://github.com/BegoTools/ESP/releases/download/sv-test-v2.0.1/";
            var metadata = new ReleaseMetadata { currentVersion = "2.0.1", channel = "test", manifestUrl = prefix + "latest.json", checksum = checksum };
            var payload = Encoding.UTF8.GetBytes(serializer.Serialize(new {
                version = metadata.currentVersion, channel = metadata.channel, minSupportedVersion = "2.0.0",
                size = setup.Length, sha256 = checksum, url = prefix + "SpectraVision-Setup-2.0.1.exe"
            }));
            var envelope = new ReleaseEnvelope { payload = Convert.ToBase64String(payload) };
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.PersistKeyInCsp = false;
                rsa.FromXmlString(Environment.GetEnvironmentVariable("SV_TEST_SIGNER_XML"));
                envelope.signature = Convert.ToBase64String(rsa.SignData(payload, "SHA256"));
            }
            Func<AppRelease> read = () =>
            {
                using (var bytes = new MemoryStream(Encoding.UTF8.GetBytes(serializer.Serialize(envelope))))
                    return AppRelease.ReadSignedManifest(bytes, metadata);
            };
            AppRelease release = read();
            Check(release != null && release.version == "2.0.1", "Trusted signed manifest is accepted");
            string path = Path.Combine(UserData.Folder, "synthetic-package.exe");
            File.WriteAllBytes(path, setup);
            using (release.VerifyPackage(path)) { }
            setup[0] ^= 1;
            File.WriteAllBytes(path, setup);
            Reject<CryptographicException>(() => release.VerifyPackage(path), "Installer bytes with wrong checksum are rejected");
            File.WriteAllBytes(path, new byte[1]);
            Reject<CryptographicException>(() => release.VerifyPackage(path), "Incomplete installer is rejected");
            metadata.checksum = new string('0', 64);
            Reject<InvalidDataException>(() => read(), "Metadata checksum must match the signed manifest");
            metadata.checksum = checksum;
            var forgedSignature = Convert.FromBase64String(envelope.signature);
            forgedSignature[0] ^= 1;
            envelope.signature = Convert.ToBase64String(forgedSignature);
            Reject<CryptographicException>(() => read(), "Forged release signature is rejected");
            metadata.currentVersion = "1.0.0";
            Check(read() == null, "Older releases are ignored");
        }

        private static void Main()
        {
            Settings old = new Settings { Language = "en", Serial = "synthetic", Emulator = "GameLoop" };
            string json = new JavaScriptSerializer().Serialize(old);
            UserData.Replace(Settings.FilePath, Encoding.UTF8.GetBytes(json));
            string machine = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null);
            string device;
            using (var sha = SHA256.Create())
                device = Distribution.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes("SpectraVision/v1/" + machine.Trim().ToLowerInvariant())));
            string path = Path.Combine(UserData.Folder, "activation.bin");
            string legacy = new JavaScriptSerializer().Serialize(new { Token = new string('b', 64), Device = device, Lease = new { expiresAt = 1L } });
            UserData.Replace(path, ProtectedData.Protect(Encoding.UTF8.GetBytes(legacy), null, DataProtectionScope.CurrentUser));

            using (var service = new LicenseService())
            {
                Check(service.HasToken && !service.Allows("live") && service.BeginWork("live") == null, "Stored v1 identity grants no offline authorization");
                Mode("unavailable");
                Check(!service.Verify().GetAwaiter().GetResult() && !service.Allows("live"), "Startup fails closed on a server error");
                Mode("allow");
                Check(service.Verify().GetAwaiter().GetResult() && service.Allows("live"), "Live verification migrates an existing device");
                SavedLicense saved = Distribution.ReadJson<SavedLicense>(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser));
                Check(saved.SchemaVersion == 2 && saved.V2Token == new string('a', 64) && saved.Token == new string('b', 64),
                    "Activation migration retains the v1 identity for rollback and saves the v2 token");
                Check(Settings.Load().Serial == "synthetic" && Settings.Load().Emulator == "GameLoop", "Stored settings survive activation migration");
            }

            using (var service = new LicenseService())
            {
                Check(service.HasToken && !service.Allows("demo"), "Restart requires live verification of the saved v2 token");
                Mode("allow");
                Check(service.Verify().GetAwaiter().GetResult(), "A valid v2 heartbeat grants a lease");
                foreach (string mode in new[] { "denied", "rate_limited", "unavailable", "invalid", "timeout" })
                {
                    Mode(mode);
                    using (var work = service.BeginWork("live"))
                    {
                        var clock = Stopwatch.StartNew();
                        Check(!service.Verify().GetAwaiter().GetResult() && !service.Allows("demo") && work.IsCancellationRequested,
                            mode + " cancels current protected work and blocks new work");
                        if (mode == "timeout") Check(clock.ElapsedMilliseconds >= 7000 && clock.ElapsedMilliseconds < 10000, "Request timed out at approximately 8 seconds");
                        Mode("allow");
                        Check(service.Verify().GetAwaiter().GetResult() && work.IsCancellationRequested,
                            "Successful re-verification does not restore cancelled work");
                    }
                }
                using (var work = service.BeginWork("live"))
                {
                    Mode("no-live");
                    Check(service.Verify().GetAwaiter().GetResult() && work.IsCancellationRequested && !service.Allows("live") && service.Allows("demo"),
                        "Feature changes cancel existing protected work and enforce the new permissions");
                }
                foreach (var pair in new[] { Tuple.Create("revoked", LicenseState.Revoked), Tuple.Create("expired", LicenseState.Expired),
                    Tuple.Create("device_removed", LicenseState.DeviceRemoved), Tuple.Create("maintenance", LicenseState.Maintenance),
                    Tuple.Create("update_required", LicenseState.UpdateRequired) })
                {
                    Mode(pair.Item1);
                    Check(!service.Verify().GetAwaiter().GetResult() && service.State == pair.Item2 && !service.Allows(null),
                        pair.Item1 + " has a distinct denied state");
                }
                Mode("short");
                Check(service.Verify().GetAwaiter().GetResult(), "A short lease can be accepted");
                Mode("unavailable");
                Thread.Sleep(3100);
                Check(!service.Allows("demo"), "Monotonic lease expiry blocks protected work");

                Mode("allow");
                Check(service.Verify().GetAwaiter().GetResult(), "Authorization can be renewed after expiry");
                Mode("unavailable");
                Invalidate(service, "PowerChanged", new PowerModeChangedEventArgs(PowerModes.Suspend));
                Check(!service.Allows(null), "Suspend invalidates the lease");
                Invalidate(service, "PowerChanged", new PowerModeChangedEventArgs(PowerModes.Resume));
                Check(!service.Allows(null), "Resume requires a fresh verification");
                Invalidate(service, "TimeChanged", EventArgs.Empty);
                Check(!service.Allows(null), "Clock change keeps protected work suspended");
            }
            CheckUpdates();
        }
    }
}
