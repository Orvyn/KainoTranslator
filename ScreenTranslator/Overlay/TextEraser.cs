using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace ScreenTranslator.Overlay;

/// <summary>
/// Removes text from a piece of a screenshot so a translation can be drawn in its place. Just
/// blurring the text leaves dark smears behind it, so it happens in three steps: find the text
/// pixels (they differ from the surrounding background), fill them in from their surroundings,
/// then blur the whole piece so what is left reads as a soft patch of the background.
/// Also reports the original text colour, so the translation can look like it belongs there.
/// </summary>
internal static class TextEraser
{
    private const int ColorDistanceThreshold = 40; // how far a pixel must be from the background to count as text
    private const int FillIterations = 40;

    /// <param name="source">Screenshot the text was found in (32bpp).</param>
    /// <param name="crop">Piece of the screenshot to produce - the text plus some margin, inside the image.</param>
    /// <param name="textBounds">Where the text is, in the screenshot's pixels.</param>
    /// <param name="blurRadius">Strength of the final blur, in pixels.</param>
    /// <param name="feather">Margin around the text that the patch extends into, and fades out over, in pixels.</param>
    public static BitmapSource Erase(
        Bitmap source, Rectangle crop, Rectangle textBounds, int blurRadius, int feather,
        out Color textColor, out Color backgroundColor)
    {
        int w = crop.Width, h = crop.Height;
        var px = ReadPixels(source, crop);
        var all = new Rectangle(0, 0, w, h);

        // Text box in the piece's own coordinates, widened a little: outlines and shadows stick out
        // past what OCR reports as the text's box.
        var text = Rectangle.Intersect(all, new Rectangle(textBounds.X - crop.X, textBounds.Y - crop.Y, textBounds.Width, textBounds.Height));
        var zone = Rectangle.Intersect(all, Rectangle.Inflate(text, 3, 3));

        // --- 1. What is the background, and which pixels are text? ---
        // Two guesses at the background: the most common colour inside the text box (text covers
        // less than half of it) and the median of the margin around it. They normally agree. When
        // they don't - e.g. a light speech bubble whose margin reaches out onto a dark scene - the
        // guess that explains more of the box as plain background wins; trusting the wrong one
        // used to turn the whole bubble into "text" and fill it with the scene's dark colour.
        var bg = EstimateBackgroundInZone(px, w, zone);
        var ring = EstimateBackgroundAround(px, w, h, zone);
        var found = BuildMask(px, w, h, zone, bg);
        if (ring is { } ringColor && Distance(ringColor, bg) > ColorDistanceThreshold)
        {
            var alternative = BuildMask(px, w, h, zone, ringColor);
            if (alternative.Coverage < found.Coverage)
            {
                bg = ringColor;
                found = alternative;
            }
        }
        backgroundColor = Color.FromRgb((byte)bg.R, (byte)bg.G, (byte)bg.B);

        // The text colour is the average of the pixels that stand out most from the background
        // (the middle of the strokes rather than their anti-aliased edges).
        textColor = PickTextColor(px, found.Diffs, found.Mask, w, zone, found.MaxDiff, bg);

        // Grow the mask so the soft edges of the letters go too.
        var grown = (bool[])found.Mask.Clone();
        for (var y = zone.Top; y < zone.Bottom; y++)
        {
            for (var x = zone.Left; x < zone.Right; x++)
            {
                if (!found.Mask[y * w + x]) continue;
                for (var dy = -2; dy <= 2; dy++)
                {
                    var yy = y + dy;
                    if (yy < 0 || yy >= h) continue;
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var xx = x + dx;
                        if (xx < 0 || xx >= w) continue;
                        grown[yy * w + xx] = true;
                    }
                }
            }
        }

        // The patch: the text box plus a thin margin. Only this part is rebuilt and blurred - the
        // rest of the piece is left to the real screen. Blurring the whole piece used to drag in
        // whatever lies outside a tight bubble (its dark outline, the scene) and darken the patch.
        var margin = Math.Clamp(feather, 2, 6);
        var patch = Rectangle.Intersect(all, Rectangle.Inflate(zone, margin, margin));

        // --- 2. Fill the text pixels in from their surroundings ---
        var f = new float[w * h * 3];
        for (var i = 0; i < w * h; i++)
        {
            f[i * 3] = px[i * 4]; f[i * 3 + 1] = px[i * 4 + 1]; f[i * 3 + 2] = px[i * 4 + 2];
        }

        // First guess: along each row, a run of text pixels becomes a straight blend between the
        // untouched pixels on either side of it - so it follows the local background, gradients
        // included, instead of one flat colour.
        var bgChannels = new float[] { bg.B, bg.G, bg.R };
        for (var y = patch.Top; y < patch.Bottom; y++)
        {
            var x = patch.Left;
            while (x < patch.Right)
            {
                if (!grown[y * w + x]) { x++; continue; }

                var runStart = x;
                while (x < patch.Right && grown[y * w + x]) x++;
                var runEnd = x; // first untouched pixel after the run, or the patch's edge

                var hasLeft = runStart > patch.Left;
                var hasRight = runEnd < patch.Right;
                for (var xx = runStart; xx < runEnd; xx++)
                {
                    var t = (xx - runStart + 1f) / (runEnd - runStart + 1f);
                    for (var c = 0; c < 3; c++)
                    {
                        var left = hasLeft ? f[(y * w + runStart - 1) * 3 + c] : hasRight ? f[(y * w + runEnd) * 3 + c] : bgChannels[c];
                        var right = hasRight ? f[(y * w + runEnd) * 3 + c] : left;
                        f[(y * w + xx) * 3 + c] = left + (right - left) * t;
                    }
                }
            }
        }

        // Then let the filled pixels settle into their surroundings vertically as well.
        for (var it = 0; it < FillIterations; it++)
        {
            for (var y = patch.Top; y < patch.Bottom; y++)
            {
                for (var x = patch.Left; x < patch.Right; x++)
                {
                    var i = y * w + x;
                    if (!grown[i]) continue;
                    var l = y * w + Math.Max(x - 1, 0);
                    var r = y * w + Math.Min(x + 1, w - 1);
                    var u = Math.Max(y - 1, 0) * w + x;
                    var dn = Math.Min(y + 1, h - 1) * w + x;
                    for (var c = 0; c < 3; c++)
                        f[i * 3 + c] = (f[l * 3 + c] + f[r * 3 + c] + f[u * 3 + c] + f[dn * 3 + c]) * 0.25f;
                }
            }
        }

        // --- 3. Blur the patch (on its own, so nothing outside it leaks in) ---
        if (blurRadius > 0)
        {
            int pw = patch.Width, ph = patch.Height;
            var g = new float[pw * ph * 3];
            for (var y = 0; y < ph; y++)
                Array.Copy(f, ((patch.Top + y) * w + patch.Left) * 3, g, y * pw * 3, pw * 3);

            BoxBlur(g, pw, ph, blurRadius);
            BoxBlur(g, pw, ph, blurRadius);

            for (var y = 0; y < ph; y++)
                Array.Copy(g, y * pw * 3, f, ((patch.Top + y) * w + patch.Left) * 3, pw * 3);
        }

        // Outside the patch the piece is transparent; the patch fades in over its margin - but only
        // on sides that have a margin: where the text runs right up to the edge of the screenshot a
        // fade would let the original text show through.
        var fadeLeft = patch.Left > 0 ? margin : 0;
        var fadeRight = patch.Right < w ? margin : 0;
        var fadeTop = patch.Top > 0 ? margin : 0;
        var fadeBottom = patch.Bottom < h ? margin : 0;

        var output = new byte[w * h * 4];
        for (var y = patch.Top; y < patch.Bottom; y++)
        {
            for (var x = patch.Left; x < patch.Right; x++)
            {
                var a = 1f;
                if (fadeLeft > 0) a = Math.Min(a, (x - patch.Left + 0.5f) / fadeLeft);
                if (fadeRight > 0) a = Math.Min(a, (patch.Right - x - 0.5f) / fadeRight);
                if (fadeTop > 0) a = Math.Min(a, (y - patch.Top + 0.5f) / fadeTop);
                if (fadeBottom > 0) a = Math.Min(a, (patch.Bottom - y - 0.5f) / fadeBottom);

                var i = y * w + x;
                output[i * 4] = ToByte(f[i * 3]);
                output[i * 4 + 1] = ToByte(f[i * 3 + 1]);
                output[i * 4 + 2] = ToByte(f[i * 3 + 2]);
                output[i * 4 + 3] = ToByte(a * 255f);
            }
        }

        var bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, output, w * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private readonly record struct Rgb(int B, int G, int R);

    private static int Distance(Rgb a, Rgb b) => Math.Max(Math.Abs(a.B - b.B), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.R - b.R)));

    private sealed class MaskResult
    {
        public bool[] Mask = Array.Empty<bool>();
        public int[] Diffs = Array.Empty<int>();
        public int MaxDiff;
        public double Coverage; // share of the text box counted as text
    }

    /// <summary>Marks the pixels of the text box that differ from the given background by more than the threshold.</summary>
    private static MaskResult BuildMask(byte[] px, int w, int h, Rectangle zone, Rgb bg)
    {
        var result = new MaskResult { Mask = new bool[w * h], Diffs = new int[w * h] };
        var count = 0;
        for (var y = zone.Top; y < zone.Bottom; y++)
        {
            for (var x = zone.Left; x < zone.Right; x++)
            {
                var i = y * w + x;
                var d = Math.Max(Math.Abs(px[i * 4] - bg.B), Math.Max(Math.Abs(px[i * 4 + 1] - bg.G), Math.Abs(px[i * 4 + 2] - bg.R)));
                result.Diffs[i] = d;
                if (d < ColorDistanceThreshold) continue;
                result.Mask[i] = true;
                count++;
                if (d > result.MaxDiff) result.MaxDiff = d;
            }
        }
        var area = Math.Max(1, zone.Width * zone.Height);
        result.Coverage = (double)count / area;
        return result;
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static byte[] ReadPixels(Bitmap source, Rectangle crop)
    {
        var data = source.LockBits(crop, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var result = new byte[crop.Width * crop.Height * 4];
            for (var y = 0; y < crop.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), result, y * crop.Width * 4, crop.Width * 4);
            return result;
        }
        finally
        {
            source.UnlockBits(data);
        }
    }

    /// <summary>Background guess 1: the most common colour inside the text box. Colours are grouped
    /// into coarse buckets first, so the slight noise of gradients and compression doesn't split it.</summary>
    private static Rgb EstimateBackgroundInZone(byte[] px, int w, Rectangle zone)
    {
        var counts = new int[4096];
        var sumB = new long[4096];
        var sumG = new long[4096];
        var sumR = new long[4096];

        for (var y = zone.Top; y < zone.Bottom; y++)
        {
            for (var x = zone.Left; x < zone.Right; x++)
            {
                var i = (y * w + x) * 4;
                var bin = ((px[i] >> 4) << 8) | ((px[i + 1] >> 4) << 4) | (px[i + 2] >> 4);
                counts[bin]++;
                sumB[bin] += px[i]; sumG[bin] += px[i + 1]; sumR[bin] += px[i + 2];
            }
        }

        var best = 0;
        for (var bin = 1; bin < counts.Length; bin++)
            if (counts[bin] > counts[best]) best = bin;

        if (counts[best] == 0) return new Rgb(128, 128, 128);
        return new Rgb((int)(sumB[best] / counts[best]), (int)(sumG[best] / counts[best]), (int)(sumR[best] / counts[best]));
    }

    /// <summary>Background guess 2: the per-channel median of the margin around the text box, or
    /// null when there is hardly any margin (text at the edge of the screen).</summary>
    private static Rgb? EstimateBackgroundAround(byte[] px, int w, int h, Rectangle zone)
    {
        var hb = new int[256];
        var hg = new int[256];
        var hr = new int[256];
        var n = 0;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                if (zone.Contains(x, y)) continue;
                var i = (y * w + x) * 4;
                hb[px[i]]++; hg[px[i + 1]]++; hr[px[i + 2]]++;
                n++;
            }
        }

        if (n < 40) return null;
        return new Rgb(Median(hb, n), Median(hg, n), Median(hr, n));
    }

    private static int Median(int[] histogram, int count)
    {
        var half = count / 2;
        var acc = 0;
        for (var i = 0; i < 256; i++)
        {
            acc += histogram[i];
            if (acc > half) return i;
        }
        return 128;
    }

    private static Color PickTextColor(byte[] px, int[] diffs, bool[] mask, int w, Rectangle zone, int maxDiff, Rgb bg)
    {
        long sumB = 0, sumG = 0, sumR = 0;
        var count = 0;

        if (maxDiff >= ColorDistanceThreshold)
        {
            var cutoff = maxDiff * 0.8;
            for (var y = zone.Top; y < zone.Bottom; y++)
            {
                for (var x = zone.Left; x < zone.Right; x++)
                {
                    var i = y * w + x;
                    if (!mask[i] || diffs[i] < cutoff) continue;
                    sumB += px[i * 4]; sumG += px[i * 4 + 1]; sumR += px[i * 4 + 2];
                    count++;
                }
            }
        }

        var bgLuma = Luma(bg.R, bg.G, bg.B);
        if (count > 0)
        {
            var r = (int)(sumR / count);
            var g = (int)(sumG / count);
            var b = (int)(sumB / count);
            var textLuma = Luma(r, g, b);
            var contrast = (Math.Max(bgLuma, textLuma) + 0.05) / (Math.Min(bgLuma, textLuma) + 0.05);
            if (contrast >= 3) return Color.FromRgb((byte)r, (byte)g, (byte)b);
        }

        // No usable original colour (nothing found, or too close to the background to read).
        return bgLuma > 0.5 ? Color.FromRgb(0x10, 0x10, 0x10) : Color.FromRgb(0xF5, 0xF5, 0xF5);
    }

    private static double Luma(int r, int g, int b) => (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;

    /// <summary>Separable box blur with clamped edges, in place on interleaved 3-channel data.</summary>
    private static void BoxBlur(float[] data, int w, int h, int radius)
    {
        var temp = new float[data.Length];
        BlurPass(data, temp, w, h, radius, horizontal: true);
        BlurPass(temp, data, w, h, radius, horizontal: false);
    }

    private static void BlurPass(float[] src, float[] dst, int w, int h, int radius, bool horizontal)
    {
        var length = horizontal ? w : h;
        var lines = horizontal ? h : w;
        var stride = horizontal ? 3 : w * 3;
        var inv = 1f / (2 * radius + 1);

        for (var line = 0; line < lines; line++)
        {
            for (var c = 0; c < 3; c++)
            {
                var start = (horizontal ? line * w * 3 : line * 3) + c;

                float sum = 0;
                for (var k = -radius; k <= radius; k++)
                    sum += src[start + Math.Clamp(k, 0, length - 1) * stride];

                for (var pos = 0; pos < length; pos++)
                {
                    dst[start + pos * stride] = sum * inv;
                    sum += src[start + Math.Min(pos + radius + 1, length - 1) * stride]
                         - src[start + Math.Max(pos - radius, 0) * stride];
                }
            }
        }
    }
}
