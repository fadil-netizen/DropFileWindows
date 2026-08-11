$ErrorActionPreference = 'Stop'

$csc64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$csc32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$csc = if (Test-Path $csc64) { $csc64 } else { $csc32 }

$src = @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

static class LogoGen
{
    private static Bitmap ResizeTo(Bitmap src, int size)
    {
        if (src.Width == size && src.Height == size) return new Bitmap(src);
        var current = new Bitmap(src);
        while (current.Width > size * 2)
        {
            int nw = Math.Max(size, current.Width / 2);
            var next = new Bitmap(nw, nw);
            using (var g = Graphics.FromImage(next))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.DrawImage(current, 0, 0, nw, nw);
            }
            current.Dispose();
            current = next;
        }
        var final = new Bitmap(size, size);
        using (var g = Graphics.FromImage(final))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.DrawImage(current, 0, 0, size, size);
        }
        current.Dispose();
        return final;
    }

    private static byte[] FrameDib(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels = new byte[bd.Stride * h];
        Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(bd);

        int andRowBytes = ((w + 31) / 32) * 4;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write(40);                       // biSize
        bw.Write(w);                        // biWidth
        bw.Write(h * 2);                    // biHeight (XOR + AND)
        bw.Write((short)1);                 // biPlanes
        bw.Write((short)32);                // biBitCount
        bw.Write(0);                        // biCompression (BI_RGB)
        bw.Write((uint)(w * 4 * h + andRowBytes * h));
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
         for (int y = h - 1; y >= 0; y--)
         {
             bw.Write(pixels, y * bd.Stride, w * 4);
         }
         byte[] andMask = new byte[h * andRowBytes];
         for (int y = 0; y < h; y++)
         {
             for (int x = 0; x < w; x++)
             {
                 if (pixels[y * bd.Stride + x * 4 + 3] < 128)
                 {
                     andMask[y * andRowBytes + (x / 32)] |= (byte)(1 << (31 - (x % 32)));
                 }
             }
         }
         bw.Write(andMask, 0, andMask.Length);
        bw.Flush();
        byte[] result = ms.ToArray();
        bw.Close();
        return result;
    }

    private static byte[] ToIco(Bitmap[] images)
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)images.Length);
        int offset = 6 + 16 * images.Length;
        var dibs = new byte[images.Length][];
        for (int i = 0; i < images.Length; i++)
        {
            int w = images[i].Width; if (w >= 256) w = 0;
            int h = images[i].Height; if (h >= 256) h = 0;
            dibs[i] = FrameDib(images[i]);
            bw.Write((byte)w);
            bw.Write((byte)h);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)dibs[i].Length);
            bw.Write((uint)offset);
            offset += dibs[i].Length;
        }
        for (int i = 0; i < dibs.Length; i++) bw.Write(dibs[i]);
        bw.Flush();
        byte[] result = ms.ToArray();
        bw.Close();
        return result;
    }

    static void Main(string[] args)
    {
        string root = args[0];
        string srcPath = Path.Combine(root, "logo-source.png");
        if (!File.Exists(srcPath)) { Console.WriteLine("logo-source.png tidak ditemukan: " + srcPath); return; }
        int[] sizes = { 16, 24, 32, 48, 64 };
        using (var src = new Bitmap(srcPath))
        {
            var images = new Bitmap[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) images[i] = ResizeTo(src, sizes[i]);
            File.WriteAllBytes(Path.Combine(root, "app.ico"), ToIco(images));
            var preview = ResizeTo(src, 256);
            preview.Save(Path.Combine(root, "logo-preview.png"), ImageFormat.Png);
            for (int i = 0; i < images.Length; i++) images[i].Dispose();
            preview.Dispose();
        }
        Console.WriteLine("app.ico + logo-preview.png selesai");
    }
}
'@

$tmpCs = Join-Path $env:TEMP 'dropfile_logo.cs'
$tmpExe = Join-Path $env:TEMP 'dropfile_logo.exe'
[System.IO.File]::WriteAllText($tmpCs, $src, [System.Text.Encoding]::UTF8)

& $csc /nologo /optimize+ /r:System.dll /r:System.Drawing.dll /out:"$tmpExe" "$tmpCs"
if ($LASTEXITCODE -ne 0) { throw "Logo generator gagal dikompilasi." }

& $tmpExe $PSScriptRoot
if ($LASTEXITCODE -ne 0) { throw "Logo generator gagal dijalankan." }

Write-Host "Logo dibuat: $(Join-Path $PSScriptRoot 'app.ico')"
Write-Host "Preview     : $(Join-Path $PSScriptRoot 'logo-preview.png')"