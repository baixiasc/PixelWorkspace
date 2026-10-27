using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelWorkspace;

/// <summary>Native BGRA pixel operations. No browser, network, or third-party imaging dependency.</summary>
public sealed class EditorPixels
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Bytes { get; }
    public EditorPixels(int width, int height, byte[] bytes)
    {
        if (width < 1 || height < 1 || bytes.Length != checked(width * height * 4)) throw new ArgumentException("Invalid pixel buffer.");
        Width = width; Height = height; Bytes = bytes;
    }
    public EditorPixels Clone() => new(Width, Height, (byte[])Bytes.Clone());
    public BitmapSource Bitmap()
    {
        var bitmap = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Bytes, Width * 4);
        bitmap.Freeze(); return bitmap;
    }
    public static EditorPixels FromBitmap(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var data = new byte[checked(converted.PixelWidth * converted.PixelHeight * 4)];
        converted.CopyPixels(data, converted.PixelWidth * 4, 0);
        return new(converted.PixelWidth, converted.PixelHeight, data);
    }
}

public sealed class EditorSticker
{
    public BitmapSource Image { get; }
    public string Name { get; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Scale { get; set; } = 80;
    public double Rotation { get; set; }
    public double Width => Image.PixelWidth * Scale / 100;
    public double Height => Image.PixelHeight * Scale / 100;
    public EditorSticker(BitmapSource image, string name) { Image = image; Name = name; }
    public bool Contains(Point p)
    {
        double a = -Rotation * Math.PI / 180, dx = p.X - X - Width / 2, dy = p.Y - Y - Height / 2;
        double x = dx * Math.Cos(a) - dy * Math.Sin(a), y = dx * Math.Sin(a) + dy * Math.Cos(a);
        return Math.Abs(x) <= Width / 2 && Math.Abs(y) <= Height / 2;
    }
    public void Clamp(int width, int height)
    {
        // Keep at least a useful portion of oversized stickers reachable.
        X = Math.Max(-Width * .3, Math.Min(width - Width * .7, X));
        Y = Math.Max(-Height * .3, Math.Min(height - Height * .7, Y));
    }
}

public sealed class EditorDocument
{
    public string Name { get; }
    public EditorPixels Original { get; }
    public EditorPixels Current { get; set; }
    public bool Dirty { get; set; }
    private readonly List<(EditorPixels Pixels, bool Dirty)> undo = new(), redo = new();
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public EditorDocument(string name, EditorPixels pixels) { Name = name; Original = pixels.Clone(); Current = pixels; }
    public void Checkpoint()
    {
        undo.Add((Current.Clone(), Dirty)); redo.Clear();
        // Bound history per image by both count and bytes, retaining at least the latest state.
        long size = undo.Sum(s => (long)s.Pixels.Bytes.Length);
        while (undo.Count > 1 && (undo.Count > 24 || size > 160L * 1024 * 1024)) { size -= undo[0].Pixels.Bytes.Length; undo.RemoveAt(0); }
    }
    public void Undo()
    {
        if (!CanUndo) return;
        redo.Add((Current, Dirty)); var state = undo[^1]; undo.RemoveAt(undo.Count - 1); Current = state.Pixels; Dirty = state.Dirty;
    }
    public void Redo()
    {
        if (!CanRedo) return;
        undo.Add((Current, Dirty)); var state = redo[^1]; redo.RemoveAt(redo.Count - 1); Current = state.Pixels; Dirty = state.Dirty;
    }
    public void Reset() { Checkpoint(); Current = Original.Clone(); Dirty = false; }
}

public static class EditorEngine
{
    public static BitmapSource LoadBitmap(string path, int maxDimension = 2500)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0]; // Static first-frame editing, as in the source editor.
        if ((long)image.PixelWidth * image.PixelHeight > 100_000_000) throw new InvalidDataException("图片超过一亿像素，请先缩小后再导入。");
        double scale = Math.Min(1, (double)maxDimension / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale < 1) image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        image = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); image.Freeze(); return image;
    }
    public static EditorPixels Crop(EditorPixels source, Int32Rect rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width < 1 || rect.Height < 1 || rect.X + rect.Width > source.Width || rect.Y + rect.Height > source.Height) throw new ArgumentOutOfRangeException(nameof(rect));
        byte[] result = new byte[checked(rect.Width * rect.Height * 4)];
        for (int y = 0; y < rect.Height; y++) Buffer.BlockCopy(source.Bytes, ((y + rect.Y) * source.Width + rect.X) * 4, result, y * rect.Width * 4, rect.Width * 4);
        return new(rect.Width, rect.Height, result);
    }
    public static void Mosaic(EditorPixels image, Point point, double radius, int cell, HashSet<long> painted)
    {
        int w = image.Width, h = image.Height; byte[] data = image.Bytes;
        int startX = Math.Max(0, (int)Math.Floor((point.X - radius) / cell) * cell), startY = Math.Max(0, (int)Math.Floor((point.Y - radius) / cell) * cell);
        for (int y = startY; y < Math.Min(h, point.Y + radius + cell); y += cell)
            for (int x = startX; x < Math.Min(w, point.X + radius + cell); x += cell)
            {
                // Circle/rectangle intersection avoids dead spots when the brush is smaller than a cell.
                double dx = point.X - Math.Clamp(point.X, x, x + cell), dy = point.Y - Math.Clamp(point.Y, y, y + cell);
                if (dx * dx + dy * dy > radius * radius || !painted.Add(((long)y << 32) | (uint)x)) continue;
                int ex = Math.Min(w, x + cell), ey = Math.Min(h, y + cell), count = (ex - x) * (ey - y);
                long b = 0, g = 0, r = 0;
                for (int py = y; py < ey; py++) for (int px = x; px < ex; px++) { int i = (py * w + px) * 4; b += data[i]; g += data[i + 1]; r += data[i + 2]; }
                for (int py = y; py < ey; py++) for (int px = x; px < ex; px++) { int i = (py * w + px) * 4; data[i] = (byte)(b / count); data[i + 1] = (byte)(g / count); data[i + 2] = (byte)(r / count); }
            }
    }
    public static void Mask(EditorPixels image, Point point, double radius, double opacity, Color color)
    {
        int left = Math.Max(0, (int)(point.X - radius)), right = Math.Min(image.Width, (int)Math.Ceiling(point.X + radius));
        int top = Math.Max(0, (int)(point.Y - radius)), bottom = Math.Min(image.Height, (int)Math.Ceiling(point.Y + radius));
        for (int y = top; y < bottom; y++) for (int x = left; x < right; x++)
        {
            double dx = x - point.X, dy = y - point.Y;
            if (dx * dx + dy * dy > radius * radius) continue;
            int i = (y * image.Width + x) * 4;
            image.Bytes[i] = (byte)Math.Round(image.Bytes[i] * (1 - opacity) + color.B * opacity);
            image.Bytes[i + 1] = (byte)Math.Round(image.Bytes[i + 1] * (1 - opacity) + color.G * opacity);
            image.Bytes[i + 2] = (byte)Math.Round(image.Bytes[i + 2] * (1 - opacity) + color.R * opacity);
            // Preserve source alpha, matching the original mask brush.
        }
    }
    public static void Gaussian(EditorPixels image, Point point, double brushRadius, double sigma)
    {
        // Separable Gaussian on a padded local region; premultiplied channels avoid dark transparent fringes.
        int k = Math.Max(1, (int)Math.Ceiling(sigma * 3));
        int left = Math.Max(0, (int)Math.Floor(point.X - brushRadius) - k), top = Math.Max(0, (int)Math.Floor(point.Y - brushRadius) - k);
        int right = Math.Min(image.Width, (int)Math.Ceiling(point.X + brushRadius) + k + 1), bottom = Math.Min(image.Height, (int)Math.Ceiling(point.Y + brushRadius) + k + 1);
        int w = right - left, h = bottom - top;
        if (w <= 0 || h <= 0) return;
        double[] kernel = new double[2 * k + 1]; double total = 0;
        for (int n = -k; n <= k; n++) { kernel[n + k] = Math.Exp(-n * n / (2 * sigma * sigma)); total += kernel[n + k]; }
        for (int n = 0; n < kernel.Length; n++) kernel[n] /= total;
        float[] temp = new float[w * h * 4];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            int target = (y * w + x) * 4;
            for (int n = -k; n <= k; n++)
            {
                int source = ((top + y) * image.Width + Math.Clamp(left + x + n, 0, image.Width - 1)) * 4;
                double weight = kernel[n + k], alpha = image.Bytes[source + 3] / 255.0;
                for (int c = 0; c < 3; c++) temp[target + c] += (float)(image.Bytes[source + c] * alpha * weight);
                temp[target + 3] += (float)(image.Bytes[source + 3] * weight);
            }
        }
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            double dx = x + left - point.X, dy = y + top - point.Y;
            if (dx * dx + dy * dy > brushRadius * brushRadius) continue;
            double b = 0, g = 0, r = 0, a = 0;
            for (int n = -k; n <= k; n++) { int i = (Math.Clamp(y + n, 0, h - 1) * w + x) * 4; double weight = kernel[n + k]; b += temp[i] * weight; g += temp[i + 1] * weight; r += temp[i + 2] * weight; a += temp[i + 3] * weight; }
            int dst = ((y + top) * image.Width + x + left) * 4;
            image.Bytes[dst] = ToByte(a > 0 ? b * 255 / a : 0); image.Bytes[dst + 1] = ToByte(a > 0 ? g * 255 / a : 0); image.Bytes[dst + 2] = ToByte(a > 0 ? r * 255 / a : 0); image.Bytes[dst + 3] = ToByte(a);
        }
    }
    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);
    public static EditorPixels Composite(EditorPixels image, IEnumerable<EditorSticker> stickers)
    {
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(image.Bitmap(), new Rect(0, 0, image.Width, image.Height));
            foreach (var sticker in stickers)
            {
                dc.PushTransform(new RotateTransform(sticker.Rotation, sticker.X + sticker.Width / 2, sticker.Y + sticker.Height / 2));
                dc.DrawImage(sticker.Image, new Rect(sticker.X, sticker.Y, sticker.Width, sticker.Height)); dc.Pop();
            }
        }
        var output = new RenderTargetBitmap(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32); output.Render(visual);
        return EditorPixels.FromBitmap(output);
    }
    public static void SavePng(EditorPixels pixels, Stream stream)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(pixels.Bitmap())); encoder.Save(stream);
    }
    /// <summary>Deterministic engine regression checks; call on the WPF STA thread.</summary>
    public static string RunSelfTests()
    {
        static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Editor self-test failed: " + label); }
        var bytes = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) { int i = (y * 8 + x) * 4; bytes[i] = (byte)(x * 20); bytes[i + 1] = (byte)(y * 20); bytes[i + 2] = 100; bytes[i + 3] = 255; }
        var pixels = new EditorPixels(8, 8, bytes);
        var cropped = Crop(pixels, new Int32Rect(2, 3, 4, 4));
        Check(cropped.Width == 4 && cropped.Height == 4 && cropped.Bytes[0] == 40 && cropped.Bytes[1] == 60, "crop dimensions / source offset");
        var mask = pixels.Clone(); Mask(mask, new Point(4, 4), 1, .5, Colors.White);
        int center = (4 * 8 + 4) * 4;
        Check(mask.Bytes[center] == 168 && mask.Bytes[center + 3] == 255 && mask.Bytes[0] == 0, "mask blend, alpha and bounds");
        var mosaic = pixels.Clone(); Mosaic(mosaic, new Point(2, 2), 2, 4, new HashSet<long>());
        Check(mosaic.Bytes[0] == 30 && mosaic.Bytes[4] == 30 && mosaic.Bytes[3] == 255, "mosaic averages and alpha");
        var impulse = new EditorPixels(9, 9, new byte[9 * 9 * 4]);
        for (int i = 3; i < impulse.Bytes.Length; i += 4) impulse.Bytes[i] = 255;
        impulse.Bytes[(4 * 9 + 4) * 4] = 255;
        Gaussian(impulse, new Point(4, 4), 3, 1);
        Check(impulse.Bytes[(4 * 9 + 4) * 4] is > 0 and < 255 && impulse.Bytes[(4 * 9 + 3) * 4] > 0 && impulse.Bytes[3] == 255, "Gaussian spreads impulse and preserves opaque alpha");
        var red = new EditorPixels(2, 2, new byte[] { 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255, 0, 0, 255, 255 });
        var sticker = new EditorSticker(red.Bitmap(), "red") { X = 2, Y = 2, Scale = 100, Rotation = 90 };
        var composite = Composite(pixels, new[] { sticker });
        int at = (2 * 8 + 2) * 4;
        Check(composite.Bytes[at + 2] == 255 && composite.Bytes[at] == 0 && composite.Width == 8, "rotated sticker composition");
        Check(sticker.Contains(new Point(3, 3)) && !sticker.Contains(new Point(7, 7)), "rotated sticker hit testing");
        using var stream = new MemoryStream(); SavePng(composite, stream); stream.Position = 0;
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var decoded = EditorPixels.FromBitmap(decoder.Frames[0]); Check(decoded.Bytes.SequenceEqual(composite.Bytes), "PNG lossless round trip");
        var document = new EditorDocument("test", pixels.Clone()); document.Checkpoint(); document.Current = cropped; document.Dirty = true;
        document.Undo(); Check(document.Current.Width == 8 && !document.Dirty, "undo"); document.Redo(); Check(document.Current.Width == 4 && document.Dirty, "redo");
        document.Reset(); Check(document.Current.Width == 8 && !document.Dirty, "reset after crop");
        return "Editor PASS: crop, mask, mosaic, Gaussian, rotated sticker composition/hit-test, PNG round-trip, undo/redo/reset.";
    }
    public static string ExportName(string name, int index)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        foreach (char c in Path.GetInvalidFileNameChars()) stem = stem.Replace(c, '_');
        if (stem.Length > 80) stem = stem[..80];
        return $"edited_{stem}_{index + 1}.png";
    }
}
