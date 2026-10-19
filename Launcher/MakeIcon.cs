// Draws the Titanic icon (gold four-funnel liner on a navy disc) into icon.ico + icon.png.
// Run via build.cmd's sibling: make-icon.cmd. Only needed when the design changes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class MakeIcon
{
    static readonly Color Gold = Color.FromArgb(212, 175, 55);
    static readonly Color Navy = Color.FromArgb(18, 26, 44);

    static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 256f, size / 256f);
            float ring = size <= 32 ? 14 : 8;

            g.FillEllipse(new SolidBrush(Navy), 4, 4, 248, 248);
            g.DrawEllipse(new Pen(Gold, ring), 4 + ring / 2, 4 + ring / 2, 248 - ring, 248 - ring);
            g.SetClip(new RectangleF(10, 10, 236, 236)); // keep everything inside the disc
            using (var disc = new GraphicsPath()) { disc.AddEllipse(12, 12, 232, 232); g.SetClip(disc); }

            var gold = new SolidBrush(Gold);
            var dark = new SolidBrush(Navy);

            // Funnels: four, raked back (toward the left, the stern), black tops.
            for (int i = 0; i < 4; i++)
            {
                float x = 92 + i * 30;
                var f = new[] { new PointF(x, 140), new PointF(x + 17, 140), new PointF(x + 9, 86), new PointF(x - 8, 86) };
                g.FillPolygon(gold, f);
                g.FillPolygon(dark, new[] { new PointF(x - 8, 86), new PointF(x + 9, 86), new PointF(x + 7.6f, 95), new PointF(x - 6.6f, 95) });
            }

            // Superstructure + hull: bow (right) rises, stern (left) a rounded counter.
            g.FillRectangle(gold, 70, 136, 124, 12);
            var hull = new[] { new PointF(36, 146), new PointF(222, 146), new PointF(206, 178), new PointF(52, 178) };
            g.FillPolygon(gold, hull);
            if (size >= 48) g.FillRectangle(dark, 44, 166, 170, 3); // waterline stripe

            // Masts.
            if (size >= 48)
            {
                var mast = new Pen(Gold, 3);
                g.DrawLine(mast, 60, 146, 56, 70);
                g.DrawLine(mast, 204, 146, 200, 74);
            }

            // Sea.
            var wave = new Pen(Gold, size <= 32 ? 9 : 5) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            for (int row = 0; row < 2; row++)
            {
                float y = 192 + row * 20;
                var p = new GraphicsPath();
                for (float x = 20; x < 236; x += 36)
                    p.AddBezier(x, y, x + 9, y - 8, x + 18, y - 8, x + 27, y);
                g.DrawPath(wave, p);
            }
        }
        return bmp;
    }

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
            using (var b = Draw(s)) using (var m = new MemoryStream()) { b.Save(m, ImageFormat.Png); pngs.Add(m.ToArray()); }
        using (var big = Draw(256)) big.Save(Path.Combine(dir, "icon.png"), ImageFormat.Png);

        // .ico with PNG-compressed frames (supported since Windows Vista).
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "icon.ico"))))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] % 256)); w.Write((byte)(sizes[i] % 256));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }
}
