using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenTranslator.Models;
using FontFamily = System.Windows.Media.FontFamily;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ScreenTranslator.Overlay;

/// <summary>
/// Overlay for the "replace text" display mode: for every block of source text it draws a blurred
/// patch of the screen (taken from a screenshot, with the text removed) and the translation on top
/// of it, exactly where the original text is. Always click-through, never takes focus.
///
/// The window is excluded from screen capture (SetWindowDisplayAffinity) - it sits right on top of the
/// text the app keeps reading, so without that the OCR would read its own translation.
/// </summary>
public partial class ReplaceOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr hwndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 2004 (build 19041) and newer
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>The mode needs to hide the overlay from screen capture, which Windows can only do from build 19041.</summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= 19041;

    public ReplaceOverlayWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        if (IsSupported) SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    /// <summary>Draws the frame's blocks (replacing whatever was shown before). Does not dispose the frame.</summary>
    public void Present(ReplacementFrame frame, OverlaySettings style)
    {
        var imageRect = new Rectangle(System.Drawing.Point.Empty, frame.Image.Size);

        var items = new List<(Rectangle Crop, int Pad, ReplacementBlock Block)>();
        foreach (var block in frame.Blocks)
        {
            var pad = (int)Math.Clamp(block.LineHeight * 0.5, 6, 20);
            var crop = Rectangle.Intersect(imageRect, Rectangle.Inflate(block.Bounds, pad, pad));
            if (crop.Width < 2 || crop.Height < 2) continue;
            items.Add((crop, pad, block));
        }
        if (items.Count == 0)
        {
            Hide();
            return;
        }

        var union = items.Select(i => i.Crop).Aggregate(Rectangle.Union);
        var screenX = frame.Origin.X + union.X;
        var screenY = frame.Origin.Y + union.Y;

        // The window needs a handle before it can be placed in physical pixels. It is placed twice:
        // moving it onto a monitor with a different scale makes WPF resize it to its own suggestion
        // the first time, and the second call puts the exact size back.
        if (!IsVisible) Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, screenX, screenY, union.Width, union.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(hwnd, HWND_TOPMOST, screenX, screenY, union.Width, union.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;

        Surface.Children.Clear();
        foreach (var (crop, pad, block) in items)
        {
            var blurRadius = (int)Math.Clamp(block.LineHeight * 0.3, 3, 12);
            var backdrop = TextEraser.Erase(frame.Image, crop, block.Bounds, blurRadius, Math.Max(2, pad - 2), out var textColor, out var backgroundColor);

            var image = new System.Windows.Controls.Image
            {
                Source = backdrop,
                Width = crop.Width / sx,
                Height = crop.Height / sy,
                Stretch = Stretch.Fill
            };
            Canvas.SetLeft(image, (crop.X - union.X) / sx);
            Canvas.SetTop(image, (crop.Y - union.Y) / sy);
            Surface.Children.Add(image);

            Surface.Children.Add(CreateText(block, union, sx, sy, textColor, backgroundColor, style));
        }
    }

    /// <summary>Gold on dark backgrounds, dark amber on light ones - the usual colour for a name in a
    /// game's dialogue. Falls back to the plain text colour if the accent wouldn't read on this
    /// particular background.</summary>
    private static Color SpeakerColor(Color textColor, Color background)
    {
        var accent = Luma(background) > 0.5 ? Color.FromRgb(0x9A, 0x62, 0x00) : Color.FromRgb(0xFF, 0xD1, 0x66);
        var lighter = Math.Max(Luma(accent), Luma(background));
        var darker = Math.Min(Luma(accent), Luma(background));
        return (lighter + 0.05) / (darker + 0.05) >= 3 ? accent : textColor;
    }

    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    private static TextBlock CreateText(ReplacementBlock block, Rectangle union, double sx, double sy, Color textColor, Color backgroundColor, OverlaySettings style)
    {
        const int sidePadding = 4; // pixels - a translation is usually a little wider than the original
        var widthDip = (block.Bounds.Width + sidePadding * 2) / sx;
        var heightDip = block.Bounds.Height / sy;

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Width = widthDip,
            FontFamily = new FontFamily(style.FontFamily),
            FontWeight = FontWeights.Medium,
            TextAlignment = block.Alignment == BlockAlignment.Center ? TextAlignment.Center : TextAlignment.Left,
            Foreground = new SolidColorBrush(textColor)
        };

        // The speaker's name goes on its own line, bold and in an accent colour, so it doesn't blend
        // into the dialogue.
        if (block.Speaker is { } speaker)
        {
            textBlock.Inlines.Add(new Run(speaker)
            {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(SpeakerColor(textColor, backgroundColor))
            });
            textBlock.Inlines.Add(new LineBreak());
        }
        textBlock.Inlines.Add(new Run(block.Text));

        // Start at the size of the original text and shrink until the translation fits in the
        // space the original took up.
        const double minSize = 9;
        var size = Math.Clamp(block.LineHeight / sy * 0.85, minSize, 72);
        for (var attempt = 0; attempt < 30; attempt++)
        {
            textBlock.FontSize = size;
            textBlock.Measure(new System.Windows.Size(widthDip, double.PositiveInfinity));
            if (textBlock.DesiredSize.Height <= heightDip * 1.08 || size <= minSize) break;
            size = Math.Max(minSize, size * 0.93);
        }

        switch (style.OutlineMode)
        {
            case TextOutlineMode.Outline:
                textBlock.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 3, ShadowDepth = 0, Opacity = 1,
                    Color = (Color)ColorConverter.ConvertFromString(style.OutlineColor)
                };
                break;
            case TextOutlineMode.Shadow:
                textBlock.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 4, ShadowDepth = 2, Opacity = 0.85,
                    Color = (Color)ColorConverter.ConvertFromString(style.OutlineColor)
                };
                break;
        }

        // Centred vertically on the original text.
        var top = (block.Bounds.Y - union.Y) / sy + (heightDip - textBlock.DesiredSize.Height) / 2;
        Canvas.SetLeft(textBlock, (block.Bounds.X - sidePadding - union.X) / sx);
        Canvas.SetTop(textBlock, top);
        return textBlock;
    }
}
