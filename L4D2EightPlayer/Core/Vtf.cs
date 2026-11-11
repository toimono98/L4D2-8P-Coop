using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace L4D2EightPlayer.Core
{
    /// <summary>
    /// Reads the largest picture out of a Valve texture (.vtf, versions 7.0-7.5): the formats the game's menu and HUD
    /// pictures use (DXT1/3/5 and the plain 8-bit ones). Only for showing the game's own look in the skin picker.
    /// </summary>
    public static class Vtf
    {
        public static Bitmap Decode(byte[] data)
        {
            if (data == null || data.Length < 64 || data[0] != 'V' || data[1] != 'T' || data[2] != 'F' || data[3] != 0) return null;
            int minor = BitConverter.ToInt32(data, 8);
            int headerSize = BitConverter.ToInt32(data, 12);
            int w = BitConverter.ToUInt16(data, 16), h = BitConverter.ToUInt16(data, 18);
            uint flags = BitConverter.ToUInt32(data, 20);
            int frames = Math.Max(1, (int)BitConverter.ToUInt16(data, 24));
            int format = BitConverter.ToInt32(data, 52);
            int mips = Math.Max(1, (int)data[56]);
            int lowFormat = BitConverter.ToInt32(data, 57);
            int lowW = data[61], lowH = data[62];
            int depth = minor >= 2 ? Math.Max(1, (int)BitConverter.ToUInt16(data, 63)) : 1;
            int faces = (flags & 0x4000) != 0 ? (minor < 5 ? 7 : 6) : 1;
            if (w == 0 || h == 0 || w > 4096 || h > 4096) return null;

            long start = -1;
            if (minor >= 3)
            {
                int count = BitConverter.ToInt32(data, 68);
                for (int i = 0; i < count && 80 + i * 8 + 8 <= data.Length; i++)
                {
                    int at = 80 + i * 8;
                    if (data[at] == 0x30 && data[at + 1] == 0 && data[at + 2] == 0) { start = BitConverter.ToUInt32(data, at + 4); break; }
                }
            }
            if (start < 0) start = headerSize + (lowFormat < 0 ? 0 : Size(lowFormat, lowW, lowH));
            // smallest mip first: skip every smaller one (all frames, faces and slices) to the largest's first frame
            for (int m = mips - 1; m >= 1; m--) start += (long)Size(format, Math.Max(1, w >> m), Math.Max(1, h >> m)) * frames * faces * depth;
            int size = Size(format, w, h);
            if (size <= 0 || start + size > data.Length) return null;
            var argb = ToArgb(data, (int)start, format, w, h);
            if (argb == null) return null;
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var bits = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(argb, 0, bits.Scan0, argb.Length);
            bmp.UnlockBits(bits);
            return bmp;
        }

        static int Size(int format, int w, int h)
        {
            int blocks = Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4);
            switch (format)
            {
                case 13: case 20: return blocks * 8;            // DXT1
                case 14: case 15: return blocks * 16;           // DXT3, DXT5
                case 0: case 1: case 11: case 12: case 16: return w * h * 4;
                case 2: case 3: case 9: case 10: return w * h * 3;
                case 4: case 6: case 17: case 18: case 19: case 22: return w * h * 2;
                case 5: case 8: return w * h;
                default: return -1;
            }
        }

        /// <summary>The pixels as 32-bit BGRA (what a Format32bppArgb bitmap holds), or null for a format not read here.</summary>
        static int[] ToArgb(byte[] d, int at, int format, int w, int h)
        {
            var px = new int[w * h];
            int Pack(int r, int g, int b, int a) => (a << 24) | (r << 16) | (g << 8) | b;
            switch (format)
            {
                case 13: case 20: Dxt(d, at, w, h, px, 1); return px;
                case 14: Dxt(d, at, w, h, px, 3); return px;
                case 15: Dxt(d, at, w, h, px, 5); return px;
            }
            for (int i = 0; i < w * h; i++)
            {
                switch (format)
                {
                    case 0: px[i] = Pack(d[at + i * 4], d[at + i * 4 + 1], d[at + i * 4 + 2], d[at + i * 4 + 3]); break;          // RGBA
                    case 1: px[i] = Pack(d[at + i * 4 + 3], d[at + i * 4 + 2], d[at + i * 4 + 1], d[at + i * 4]); break;          // ABGR
                    case 11: px[i] = Pack(d[at + i * 4 + 1], d[at + i * 4 + 2], d[at + i * 4 + 3], d[at + i * 4]); break;         // ARGB
                    case 12: px[i] = Pack(d[at + i * 4 + 2], d[at + i * 4 + 1], d[at + i * 4], d[at + i * 4 + 3]); break;         // BGRA
                    case 16: px[i] = Pack(d[at + i * 4 + 2], d[at + i * 4 + 1], d[at + i * 4], 255); break;                       // BGRX
                    case 2: case 9: px[i] = Pack(d[at + i * 3], d[at + i * 3 + 1], d[at + i * 3 + 2], 255); break;                // RGB
                    case 3: case 10: px[i] = Pack(d[at + i * 3 + 2], d[at + i * 3 + 1], d[at + i * 3], 255); break;               // BGR
                    case 5: px[i] = Pack(d[at + i], d[at + i], d[at + i], 255); break;                                             // I8
                    case 6: px[i] = Pack(d[at + i * 2], d[at + i * 2], d[at + i * 2], d[at + i * 2 + 1]); break;                  // IA88
                    case 8: px[i] = Pack(255, 255, 255, d[at + i]); break;                                                          // A8
                    default: return null;
                }
            }
            return px;
        }

        static void Dxt(byte[] d, int at, int w, int h, int[] px, int kind)
        {
            int bw = Math.Max(1, (w + 3) / 4), bh = Math.Max(1, (h + 3) / 4);
            var colors = new int[4];
            var alphas = new byte[16];
            for (int by = 0; by < bh; by++)
                for (int bx = 0; bx < bw; bx++)
                {
                    int p = at + (by * bw + bx) * (kind == 1 ? 8 : 16);
                    bool hasAlpha = false;
                    if (kind == 3)
                    {
                        for (int i = 0; i < 16; i++) { int nib = (d[p + i / 2] >> ((i & 1) * 4)) & 15; alphas[i] = (byte)(nib * 17); }
                        hasAlpha = true; p += 8;
                    }
                    else if (kind == 5)
                    {
                        byte a0 = d[p], a1 = d[p + 1];
                        var table = new byte[8];
                        table[0] = a0; table[1] = a1;
                        if (a0 > a1) for (int i = 1; i < 7; i++) table[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
                        else { for (int i = 1; i < 5; i++) table[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5); table[6] = 0; table[7] = 255; }
                        ulong bits = 0;
                        for (int i = 0; i < 6; i++) bits |= (ulong)d[p + 2 + i] << (8 * i);
                        for (int i = 0; i < 16; i++) alphas[i] = table[(bits >> (3 * i)) & 7];
                        hasAlpha = true; p += 8;
                    }
                    int c0 = d[p] | (d[p + 1] << 8), c1 = d[p + 2] | (d[p + 3] << 8);
                    void Rgb(int c, out int r, out int g, out int b) { r = ((c >> 11) & 31) * 255 / 31; g = ((c >> 5) & 63) * 255 / 63; b = (c & 31) * 255 / 31; }
                    Rgb(c0, out var r0, out var g0, out var b0);
                    Rgb(c1, out var r1, out var g1, out var b1);
                    colors[0] = (255 << 24) | (r0 << 16) | (g0 << 8) | b0;
                    colors[1] = (255 << 24) | (r1 << 16) | (g1 << 8) | b1;
                    if (c0 > c1 || kind != 1)
                    {
                        colors[2] = (255 << 24) | ((2 * r0 + r1) / 3 << 16) | ((2 * g0 + g1) / 3 << 8) | (2 * b0 + b1) / 3;
                        colors[3] = (255 << 24) | ((r0 + 2 * r1) / 3 << 16) | ((g0 + 2 * g1) / 3 << 8) | (b0 + 2 * b1) / 3;
                    }
                    else
                    {
                        colors[2] = (255 << 24) | ((r0 + r1) / 2 << 16) | ((g0 + g1) / 2 << 8) | (b0 + b1) / 2;
                        colors[3] = 0;   // transparent
                    }
                    uint idx = BitConverter.ToUInt32(d, p + 4);
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx * 4 + (i & 3), y = by * 4 + (i >> 2);
                        if (x >= w || y >= h) continue;
                        int c = colors[(idx >> (2 * i)) & 3];
                        if (hasAlpha) c = (c & 0x00FFFFFF) | (alphas[i] << 24);
                        px[y * w + x] = c;
                    }
                }
        }
    }
}
