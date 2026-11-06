# 生成 app.ico：陶土色圆角方块 + 两道白色“接力棒”，和界面里的标志一致
# 每个尺寸单独绘制并对齐像素，小尺寸（16/20/24）也清晰
# 用法：powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root 'app.ico'

Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class RelayIcon
{
    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static byte[] Png(int s)
    {
        using (var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            // 小尺寸几乎铺满，大尺寸留一点边（和 Windows 11 图标的留白接近）
            float pad = s <= 24 ? 0.5f : s * 0.06f;
            var box = new RectangleF(pad, pad, s - pad * 2, s - pad * 2);
            var clay = Color.FromArgb(0xD9, 0x73, 0x4E);
            var clayDark = Color.FromArgb(0xC4, 0x5F, 0x3C);
            using (var path = Round(box, box.Width * 0.28f))
            {
                if (s >= 32)
                {
                    using (var br = new LinearGradientBrush(box, clay, clayDark, 90f)) g.FillPath(br, path);
                }
                else
                {
                    using (var br = new SolidBrush(clay)) g.FillPath(br, path);
                }
            }

            // 两道接力棒：上面一道长，下面一道短一点、略透明，错开一点体现“交接”
            float u = box.Width / 22f;
            float h = Math.Max(2f, (float)Math.Round(3.2f * u));
            float y1 = box.Y + (float)Math.Round(7f * u);
            float y2 = y1 + h + Math.Max(1.5f, (float)Math.Round(2.2f * u));
            float x1 = box.X + (float)Math.Round(5f * u), w1 = (float)Math.Round(12f * u);
            float x2 = box.X + (float)Math.Round(6.2f * u), w2 = (float)Math.Round(9.6f * u);
            if (s <= 24)
            {
                // small sizes: integer pixels, no anti-aliasing, so the bars stay crisp
                int hh = s <= 16 ? 2 : 3;
                int bx1 = (int)Math.Round(s * 0.22), bw1 = s - bx1 * 2;
                int bx2 = bx1 + 1, bw2 = bw1 - 3;
                int by1 = (int)Math.Round(s * 0.30), by2 = by1 + hh + (s <= 16 ? 2 : 3);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.None;
                using (var white = new SolidBrush(Color.White))
                using (var soft = new SolidBrush(Color.FromArgb(200, 255, 255, 255)))
                {
                    g.FillRectangle(white, bx1, by1, bw1, hh);
                    g.FillRectangle(soft, bx2, by2, bw2, hh);
                }
            }
            else
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Color.FromArgb(185, 255, 255, 255)))
            {
                using (var b1 = Round(new RectangleF(x1, y1, w1, h), h / 2)) g.FillPath(white, b1);
                using (var b2 = Round(new RectangleF(x2, y2, w2, h), h / 2)) g.FillPath(soft, b2);
            }

            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    // ICO 文件：每个尺寸存一张 PNG（Vista 起支持）
    public static void Save(string path, int[] sizes)
    {
        var images = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++) images[i] = Png(sizes[i]);
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
    }
}
'@

[RelayIcon]::Save($out, @(16, 20, 24, 32, 40, 48, 64, 128, 256))
foreach ($s in 16, 32, 256) { [IO.File]::WriteAllBytes((Join-Path $root "design\icon-$s.png"), [RelayIcon]::Png($s)) }
Write-Host "已生成 $out"
