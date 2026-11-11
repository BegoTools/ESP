using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ESPWorkbench
{
    // Reusable premultiplied-alpha DIB: antialiased edges remain transparent
    // instead of blending into the old magenta color key.
    internal sealed class AlphaSurface : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPels,YPels;public uint Colors,Important; }
        [StructLayout(LayoutKind.Sequential,Pack=1)] private struct Blend { public byte Operation,Flags,Alpha,Format; }
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc,ref BitmapInfo info,uint usage,out IntPtr bits,IntPtr section,uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc,IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref Point destination,ref Size size,IntPtr source,ref Point origin,uint key,ref Blend blend,uint flags);
        private IntPtr dc,dib,previous;
        private Bitmap bitmap;
        private Size size;
        internal void Draw(IntPtr window,Rectangle bounds,Action<Graphics> paint)
        {
            if(bounds.Width<1||bounds.Height<1)return;
            Ensure(bounds.Size);
            using(Graphics graphics=Graphics.FromImage(bitmap)) { graphics.Clear(Color.Transparent);paint(graphics);graphics.Flush(); }
            Point destination=bounds.Location,origin=Point.Empty;Blend blend=new Blend { Alpha=255,Format=1 };
            if(!UpdateLayeredWindow(window,IntPtr.Zero,ref destination,ref size,dc,ref origin,0,ref blend,2))throw new Win32Exception(Marshal.GetLastWin32Error(),"Unable to present transparent overlay.");
        }
        private void Ensure(Size requested)
        {
            if(bitmap!=null&&size==requested)return;
            Dispose();size=requested;
            try
            {
                dc=CreateCompatibleDC(IntPtr.Zero);if(dc==IntPtr.Zero)throw new Win32Exception();
                BitmapInfo info=new BitmapInfo { Size=40,Width=size.Width,Height=-size.Height,Planes=1,BitCount=32 };
                IntPtr bits;dib=CreateDIBSection(dc,ref info,0,out bits,IntPtr.Zero,0);if(dib==IntPtr.Zero)throw new Win32Exception();
                previous=SelectObject(dc,dib);
                bitmap=new Bitmap(size.Width,size.Height,size.Width*4,PixelFormat.Format32bppPArgb,bits);
            }
            catch { Dispose();throw; }
        }
        public void Dispose()
        {
            if(bitmap!=null)bitmap.Dispose();bitmap=null;
            if(dc!=IntPtr.Zero&&previous!=IntPtr.Zero)SelectObject(dc,previous);
            if(dib!=IntPtr.Zero)DeleteObject(dib);
            if(dc!=IntPtr.Zero)DeleteDC(dc);
            dc=dib=previous=IntPtr.Zero;
        }
    }
}
