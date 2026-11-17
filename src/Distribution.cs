using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

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
            return Request(new Uri(new Uri(ReleaseConfig.Origin), path));
        }

        internal static HttpWebRequest Request(Uri uri)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback) throw new InvalidOperationException(L.Text("HTTPS is required."));
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.AllowAutoRedirect = false;
            request.Timeout = 8500;
            request.ReadWriteTimeout = 8500;
            request.UserAgent = "SpectraVision/" + VersionText;
            request.Proxy = uri.IsLoopback ? null : request.Proxy;
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

    internal sealed class ActivationForm : Form
    {
        private readonly TextBox code = new TextBox();
        private readonly Button activate = new SpectraPrimaryButton();
        private readonly Button retry = new SpectraButton();
        private readonly Button update = new SpectraButton();
        private readonly Label status = new Label();
        private readonly LicenseService client;
        private bool working;

        internal ActivationForm(LicenseService client, bool verifyExisting)
        {
            this.client = client;
            Text = L.Text("RAM HAX | Activation");
            ClientSize = new Size(720, 548);
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            DoubleBuffered = true;
            BackColor = SpectraTheme.Background;
            ForeColor = Color.White;
            Font = new Font("Segoe UI", 16, GraphicsUnit.Pixel);
            Label title = new Label { Text=L.Text("كود التفعيل"),Bounds=new Rectangle(167,251,386,32),
                RightToLeft=L.Direction,TextAlign=L.Rtl?ContentAlignment.MiddleRight:ContentAlignment.MiddleLeft,
                BackColor=SpectraTheme.Surface,ForeColor=Color.White,Font=new Font(Font,FontStyle.Bold) };
            status.Bounds = new Rectangle(150, 157, 420, 67);
            status.TextAlign=ContentAlignment.MiddleCenter;
            status.RightToLeft = L.Direction;
            status.BackColor = SpectraTheme.Surface;
            status.ForeColor = SpectraTheme.Accent;
            status.Text = client.Message;
            code.Bounds = new Rectangle(170, 301, 380, 36);
            code.BorderStyle=BorderStyle.None;
            code.BackColor=SpectraTheme.Background;
            code.ForeColor=Color.White;
            code.Font=new Font("Segoe UI",20,GraphicsUnit.Pixel);
            code.TextAlign=HorizontalAlignment.Center;
            code.UseSystemPasswordChar=true;
            code.RightToLeft=RightToLeft.No;
            code.MaxLength = 64;
            code.AccessibleName = L.Text("كود التفعيل");
            activate.Text = L.Text("تفعيل");
            activate.Bounds = new Rectangle(164, 349, 392, 57);
            activate.BackColor = SpectraTheme.Surface;
            activate.FlatStyle = FlatStyle.Flat;
            activate.Click += async delegate { if (code.Text.Trim().Length != 0) await Check(false); };
            StyleButton(retry,L.Text("Retry"),new Rectangle(164, 419, 184, 38));
            retry.Enabled = client.HasToken;
            retry.Click += async delegate { await Check(true); };
            StyleButton(update,L.Text("فحص التحديثات"),new Rectangle(371, 419, 184, 38));
            update.Click += async delegate { await UpdateApplication(); };
            Button exit = new SpectraButton { DialogResult=DialogResult.Cancel,AccessibleName=L.Text("Close"),TabStop=false };
            StyleButton(exit,"×",new Rectangle(667, 15, 34, 30));
            Button support=SupportLink.CreateButton();
            support.Bounds=new Rectangle(251,486,218,39);
            Controls.AddRange(new Control[] { title, status, code, activate, retry, update, exit, support });
            ActiveControl=code;
            AcceptButton = activate;
            CancelButton = exit;
            if (verifyExisting) Shown += async delegate { await Check(true); };
        }

        private static void StyleButton(Button button,string text,Rectangle bounds)
        {
            button.Text=text;
            button.Bounds=bounds;
            button.BackColor=SpectraTheme.Surface;
            button.ForeColor=Color.White;
            button.FlatStyle=FlatStyle.Flat;
            button.FlatAppearance.BorderColor=SpectraTheme.Border;
            button.FlatAppearance.MouseOverBackColor=SpectraTheme.Selected;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g=e.Graphics;
            g.SmoothingMode=SmoothingMode.AntiAlias;
            RectangleF frame=new RectangleF(4,4,Width-8,Height-8);
            SpectraTheme.Card(g,frame,true);
            SpectraTheme.Wordmark(g,new RectangleF(282,37,156,103),true);
            SpectraTheme.Card(g,new RectangleF(143,151,434,79),true,255,10,SpectraTheme.Surface);
            SpectraTheme.Card(g,new RectangleF(144,237,432,240),true,255,14,SpectraTheme.Surface);
            SpectraTheme.Card(g,new RectangleF(165,287,390,54),true,255,8);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(e.Button==MouseButtons.Left&&e.Y<146)SpectraTheme.DragWindow(Handle);
        }

        private async Task Check(bool existing)
        {
            if (working) return;
            working = true;
            code.Enabled = activate.Enabled = retry.Enabled = update.Enabled = false;
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
                if (!IsDisposed && DialogResult == DialogResult.None)
                {
                    code.Enabled = activate.Enabled = update.Enabled = true;
                    retry.Enabled = client.HasToken;
                    code.Focus();
                }
            }
        }

        private async Task UpdateApplication()
        {
            if (working) return;
            working = true;
            update.Enabled = retry.Enabled = activate.Enabled = false;
            try
            {
                AppRelease release = await Task.Run(() => AppRelease.Check());
                if (IsDisposed) return;
                if (release == null) { status.Text = L.Format("Version {0} is already up to date.", Distribution.VersionText); return; }
                if (MessageBox.Show(this, L.Format("Install version {0}?\n\n{1}", release.version, release.notes),
                    L.Text("Updates"), MessageBoxButtons.YesNo) != DialogResult.Yes) return;
                string setup = await Task.Run(() => release.Download());
                if (IsDisposed) { File.Delete(setup); return; }
                release.Install(setup);
                DialogResult = DialogResult.Cancel;
            }
            catch (Exception)
            {
                if (!IsDisposed) status.Text = L.Text("تعذر تنزيل التحديث أو التحقق من الملف. حاول تاني.");
            }
            finally
            {
                working = false;
                if (!IsDisposed) { update.Enabled = activate.Enabled = true; retry.Enabled = client.HasToken; }
            }
        }
    }
#endif
}
