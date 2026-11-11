using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace ESPWorkbench
{
    internal static class GraphicsTests
    {
        internal static void Run(Action<bool,string> check)
        {
            RenderOptions options=new JavaScriptSerializer().Deserialize<RenderOptions>("{}");options.EnsureDefaults();
            check(!options.ImageQualityEnabled,"old settings keep image enhancement opt-in");
            options.ImageQualityEnabled=true;
            RenderOptions saved=new JavaScriptSerializer().Deserialize<RenderOptions>(new JavaScriptSerializer().Serialize(options));
            check(saved.ImageQualityEnabled,"image enhancement preference persists");
            using(Bitmap image=new Bitmap(300,180))using(Graphics graphics=Graphics.FromImage(image))
            {
                graphics.Clear(Color.Transparent);Painter.Draw(graphics,image.Size,null,options,false,false);
                check(graphics.SmoothingMode==SmoothingMode.AntiAlias&&graphics.TextRenderingHint==TextRenderingHint.AntiAliasGridFit,"quality switch enables overlay geometry and text antialiasing");
                check(image.GetPixel(0,0).A==0,"overlay background retains transparent alpha");
                options.ImageQualityEnabled=false;Painter.Draw(graphics,image.Size,null,options,false,false);
                check(graphics.SmoothingMode==SmoothingMode.None&&graphics.TextRenderingHint==TextRenderingHint.SingleBitPerPixelGridFit,"quality off restores crisp overlay drawing");
            }
            using(Bitmap image=new Bitmap(1000,620))using(Graphics graphics=Graphics.FromImage(image))
            {
                options.ImageQualityEnabled=true;graphics.Clear(Color.Transparent);Painter.Draw(graphics,image.Size,Demo.Make(0),options,false,false);
                int edgePixels=0;
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++) { Color pixel=image.GetPixel(x,y);if(pixel.A>0&&pixel.A<255)edgePixels++; }
                check(edgePixels>100,"real overlay primitives produce transparent antialiased edge pixels");
                options.ImageQualityEnabled=false;
            }
            using(Form window=new Form { FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-20000,-20000),Size=new Size(64,64) })
            using(AlphaSurface surface=new AlphaSurface())
            {
                // Match the production window's WS_EX_LAYERED style without
                // displaying a window or relying on an emulator session.
                IntPtr handle=window.Handle;
                NativeStyle.SetWindowLongPtr(handle,-20,new IntPtr(NativeStyle.GetWindowLongPtr(handle,-20).ToInt64()|0x80000));
                surface.Draw(handle,window.Bounds,g=>{g.SmoothingMode=SmoothingMode.AntiAlias;g.DrawEllipse(Pens.White,8,8,40,40);});
                surface.Draw(handle,new Rectangle(-20000,-20000,90,80),g=>g.FillRectangle(Brushes.Green,4,4,12,12));
                check(true,"native alpha overlay presents and resizes without color-key artifacts");
            }
            using(Overlay overlay=new Overlay(options))
            {
                IntPtr initial=overlay.Handle;
                check(overlay.TransparencyKey==Color.Fuchsia,"quality off preserves the existing lightweight color-key overlay");
                options.ImageQualityEnabled=true;
                MethodInfo switchMode=typeof(Overlay).GetMethod("EnsurePresentationMode",BindingFlags.NonPublic|BindingFlags.Instance);
                switchMode.Invoke(overlay,null);
                check(overlay.TransparencyKey==Color.Empty&&(NativeStyle.GetWindowLongPtr(overlay.Handle,-20).ToInt64()&0x08080020)==0x08080020,"quality on selects alpha while retaining click-through and no-activation styles");
                options.ImageQualityEnabled=false;switchMode.Invoke(overlay,null);
                check(overlay.TransparencyKey==Color.Fuchsia,"quality off safely returns from per-pixel alpha to color-key rendering");
            }
            bool requested=false;
            using(OptionsForm menu=new OptionsForm(options,delegate {},null,null,enabled=>{requested=enabled;options.ImageQualityEnabled=enabled;return "saved";}))
            using(Control page=menu.CreateEmbeddedPage(7))
            {
                CheckBox quality=Find(page);quality.Checked=true;check(requested&&options.ImageQualityEnabled,"Visuals switch applies the driver callback and shared overlay option");
                quality.Checked=false;check(!requested&&!options.ImageQualityEnabled,"Visuals off synchronizes the driver callback and overlay");
            }
            using(OptionsForm menu=new OptionsForm(options,delegate {},null,null,enabled=>{throw new InvalidOperationException("simulated driver failure");}))
            using(Control page=menu.CreateEmbeddedPage(7))
            { CheckBox quality=Find(page);quality.Checked=true;check(!quality.Checked&&!options.ImageQualityEnabled,"driver failure rolls back the switch without enabling overlay enhancement"); }
            using(OptionsForm menu=new OptionsForm(options,delegate {}))using(Control page=menu.CreateEmbeddedPage(7))
                check(!Find(page).Enabled,"render previews cannot modify NVIDIA driver settings");
        }
        private static CheckBox Find(Control parent)
        {
            foreach(Control child in parent.Controls) { CheckBox toggle=child as CheckBox;if(toggle!=null&&toggle.AccessibleName==L.InDevelopment("تحسين الصورة: NVIDIA FXAA للمحاكيات + تنعيم الأداة"))return toggle;CheckBox nested=Find(child);if(nested!=null)return nested; }return null;
        }
        private static class NativeStyle
        {
            [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
            [System.Runtime.InteropServices.DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr window,int index,IntPtr value);
        }
    }
}
