using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ScreenTranslator.Capture;

public static class ScreenCapture
{
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>
    /// Captures a rectangle given in virtual-screen physical pixels (same space as
    /// System.Windows.Forms.SystemInformation.VirtualScreen / Screen.AllScreens bounds).
    /// Uses BitBlt (via Graphics.CopyFromScreen) which works well for borderless and
    /// windowed games; exclusive-fullscreen games that bypass DWM may show a black
    /// capture area - see README for how to avoid that (use borderless/windowed mode).
    /// </summary>
    public static Bitmap CaptureRegion(Rectangle region)
    {
        var bitmap = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    /// <summary>Samples the color of a single screen pixel, given in virtual-screen physical pixels. Used by the eyedropper color picker.</summary>
    public static Color SamplePixel(int x, int y)
    {
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(x, y, 0, 0, new Size(1, 1), CopyPixelOperation.SourceCopy);
        return bitmap.GetPixel(0, 0);
    }

    /// <summary>
    /// Cheap perceptual hash used to detect "did the on-screen text actually change" so the
    /// pipeline can skip OCR + translation (the expensive/slow steps) on unchanged frames -
    /// this is the main lever for keeping perceived delay low without hammering CPU or APIs.
    /// </summary>
    public static long QuickHash(Bitmap bitmap)
    {
        // Downscale to a small fixed grid and sum coarse luminance buckets - enough to
        // distinguish "same subtitle" from "new subtitle" while being very cheap to compute.
        const int gridW = 24, gridH = 8;
        using var small = new Bitmap(bitmap, new Size(gridW, gridH));
        long hash = 17;
        for (int y = 0; y < gridH; y++)
        {
            for (int x = 0; x < gridW; x++)
            {
                var c = small.GetPixel(x, y);
                int luminance = (c.R + c.G + c.B) / 3 / 16; // coarse bucket, tolerant of encoding noise
                hash = hash * 31 + luminance;
            }
        }
        return hash;
    }
}
