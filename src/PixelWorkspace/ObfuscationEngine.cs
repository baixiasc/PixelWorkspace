using System;
using System.Collections.Generic;

namespace PixelWorkspace;

/// <summary>Exact port of the original gilbert2d permutation; not cryptographic encryption.</summary>
public static class ObfuscationEngine
{
    // Four-channel arrays are indexed by Int32; no arbitrary dimension/megapixel cap.
    // Allocation failures are reported by the page, never resolved by silently resizing.
    public const int MaximumPixels = 0x7fefffff / 4;
    public static byte[] Transform(byte[] pixels, int width, int height, bool decrypt)
    {
        int count = ValidateSize(width, height);
        if (pixels.Length != checked(count * 4)) throw new ArgumentException("需要四通道像素数据。", nameof(pixels));
        var curve = Curve(width, height);
        var result = new byte[pixels.Length];
        int shift = (int)Math.Floor((Math.Sqrt(5) - 1) / 2 * width * height + 0.5);
        for (int i = 0; i < count; i++)
        {
            int a = curve[i] * 4, b = curve[(i + shift) % count] * 4;
            Buffer.BlockCopy(pixels, decrypt ? b : a, result, decrypt ? a : b, 4);
        }
        return result;
    }
    public static int ValidateSize(int width, int height)
    {
        long count = (long)width * height;
        if (width < 1 || height < 1 || count > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), $"尺寸必须为正数，且不超过 {MaximumPixels:N0} 像素；不会自动缩放。");
        return (int)count;
    }
    public static int[] Curve(int width, int height)
    {
        var result = new int[ValidateSize(width, height)]; int position = 0;
        void Generate(int x, int y, int ax, int ay, int bx, int by)
        {
            int w = Math.Abs(ax + ay), h = Math.Abs(bx + by);
            int dax = Math.Sign(ax), day = Math.Sign(ay), dbx = Math.Sign(bx), dby = Math.Sign(by);
            if (h == 1) { for (int i = 0; i < w; i++) { result[position++] = x + y * width; x += dax; y += day; } return; }
            if (w == 1) { for (int i = 0; i < h; i++) { result[position++] = x + y * width; x += dbx; y += dby; } return; }
            // C# integer division truncates toward zero; JS floor requires arithmetic shift.
            int ax2 = ax >> 1, ay2 = ay >> 1, bx2 = bx >> 1, by2 = by >> 1;
            int w2 = Math.Abs(ax2 + ay2), h2 = Math.Abs(bx2 + by2);
            if (2L * w > 3L * h)
            {
                if ((w2 % 2) != 0 && w > 2) { ax2 += dax; ay2 += day; }
                Generate(x, y, ax2, ay2, bx, by);
                Generate(x + ax2, y + ay2, ax - ax2, ay - ay2, bx, by);
            }
            else
            {
                if ((h2 % 2) != 0 && h > 2) { bx2 += dbx; by2 += dby; }
                Generate(x, y, bx2, by2, ax2, ay2);
                Generate(x + bx2, y + by2, ax, ay, bx - bx2, by - by2);
                Generate(x + ax - dax + bx2 - dbx, y + ay - day + by2 - dby, -bx2, -by2, -(ax - ax2), -(ay - ay2));
            }
        }
        if (width >= height) Generate(0, 0, width, 0, 0, height); else Generate(0, 0, 0, height, width, 0);
        return result;
    }
}
