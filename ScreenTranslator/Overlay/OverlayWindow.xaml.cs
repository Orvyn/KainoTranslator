using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenTranslator.Models;
using FontFamily = System.Windows.Media.FontFamily;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ScreenTranslator.Overlay;

/// <summary>
/// Always-on-top, transparent overlay that shows the translated text above/below/over the
/// capture region (or a fixed screen position). Click-through is implemented at the Win32
/// level (WS_EX_TRANSPARENT) so it never steals focus or clicks from the game/app underneath.
/// </summary>
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_TOOLWINDOW = 0x80; // keep it out of alt-tab and the taskbar

    private readonly TextBlock[] _outlineCopies;
    private double _configuredMaxWidth = 900;

    public OverlayWindow()
    {
        InitializeComponent();
        _outlineCopies = new[] { Outline_NW, Outline_N, Outline_NE, Outline_W, Outline_E, Outline_SW, Outline_S, Outline_SE };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough(true);
    }

    public void ApplyClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
        style = enabled ? (style | WS_EX_TRANSPARENT) : (style & ~WS_EX_TRANSPARENT);
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    public void ApplyStyle(OverlaySettings style)
    {
        var font = new FontFamily(style.FontFamily);
        var textBrush = (Brush)new BrushConverter().ConvertFromString(style.TextColor)!;
        var outlineBrush = (Brush)new BrushConverter().ConvertFromString(style.OutlineColor)!;

        _configuredMaxWidth = style.MaxWidth;

        TranslatedText.FontSize = style.FontSize;
        TranslatedText.FontFamily = font;
        TranslatedText.Foreground = textBrush;

        foreach (var copy in _outlineCopies)
        {
            copy.FontSize = style.FontSize;
            copy.FontFamily = font;
            copy.Foreground = outlineBrush;
            copy.Visibility = style.OutlineMode == TextOutlineMode.Outline ? Visibility.Visible : Visibility.Collapsed;
        }
        SetWrapWidth(_configuredMaxWidth);

        // Outline and shadow are mutually exclusive per-letter readability aids.
        TranslatedText.Effect = style.OutlineMode == TextOutlineMode.Shadow
            ? new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 2, Opacity = 0.85, Color = (Color)ColorConverter.ConvertFromString(style.OutlineColor) }
            : null;

        var bgRgb = (Color)ColorConverter.ConvertFromString(style.BackgroundColorRgb);
        var transparency = Math.Clamp(style.BackgroundTransparencyPercent, 0, 100);
        bgRgb.A = (byte)Math.Clamp((100 - transparency) * 255 / 100, 0, 255);
        Backdrop.Background = new SolidColorBrush(bgRgb);

        ApplyClickThrough(style.ClickThrough);
    }

    private void SetWrapWidth(double maxWidth)
    {
        TranslatedText.MaxWidth = maxWidth;
        foreach (var copy in _outlineCopies) copy.MaxWidth = maxWidth;
    }

    public void SetText(string text)
    {
        TranslatedText.Text = text;
        foreach (var copy in _outlineCopies) copy.Text = text;
        Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Hidden : Visibility.Visible;
    }

    /// <summary>
    /// If wrapping at the configured width would make the block taller than the screen can show,
    /// progressively widen it (up to the screen's own width) - fewer, longer lines means less
    /// total height for the same amount of text, so long translations don't run off-screen.
    /// </summary>
    private void FitWrapWidthToScreen(double screenWidthDip, double screenHeightDip)
    {
        var maxAllowedWidth = Math.Max(150, screenWidthDip * 0.92);
        var width = Math.Min(_configuredMaxWidth, maxAllowedWidth);
        SetWrapWidth(width);
        UpdateLayout();

        var heightLimit = screenHeightDip * 0.85;
        var attempts = 0;
        while (ActualHeight > heightLimit && width < maxAllowedWidth - 1 && attempts < 6)
        {
            width = Math.Min(maxAllowedWidth, width + (maxAllowedWidth - width) * 0.5 + 40);
            SetWrapWidth(width);
            UpdateLayout();
            attempts++;
        }
    }

    /// <summary>
    /// Positions the overlay relative to the capture region (physical pixels) per the
    /// configured OverlayPosition. Uses the WPF DPI transform for the monitor the region is on
    /// so the overlay lines up correctly even with mixed-DPI multi-monitor setups.
    /// </summary>
    public void PositionRelativeTo(Rectangle regionPhysical, OverlayPosition position)
    {
        var screenBounds = System.Windows.Forms.Screen.FromRectangle(regionPhysical).Bounds;

        // Convert the physical-pixel region + screen bounds into DIPs using this window's
        // current DPI (set once we have a source; before that, WPF defaults to 96 DPI which
        // is fine for the very first placement and gets corrected once the source exists).
        var source = PresentationSource.FromVisual(this);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        var regionTopLeft = m.Transform(new System.Windows.Point(regionPhysical.Left, regionPhysical.Top));
        var regionBottomLeft = m.Transform(new System.Windows.Point(regionPhysical.Left, regionPhysical.Bottom));
        var regionSize = m.Transform(new System.Windows.Point(regionPhysical.Width, regionPhysical.Height));
        var screenTopLeft = m.Transform(new System.Windows.Point(screenBounds.Left, screenBounds.Top));
        var screenSize = m.Transform(new System.Windows.Point(screenBounds.Width, screenBounds.Height));

        // Measure the (already styled) content, adaptively widening the wrap width first if a
        // long translation would otherwise be taller than the screen.
        FitWrapWidthToScreen(screenSize.X, screenSize.Y);
        var contentWidth = ActualWidth > 0 ? ActualWidth : 400;
        var contentHeight = ActualHeight > 0 ? ActualHeight : 80;

        double left, top;
        switch (position)
        {
            case OverlayPosition.AboveCaptureArea:
                left = regionTopLeft.X + (regionSize.X - contentWidth) / 2;
                top = regionTopLeft.Y - contentHeight - 8;
                break;
            case OverlayPosition.OverCaptureArea:
                left = regionTopLeft.X + (regionSize.X - contentWidth) / 2;
                top = regionTopLeft.Y + (regionSize.Y - contentHeight) / 2;
                break;
            case OverlayPosition.FixedTopCenterOfScreen:
                left = screenTopLeft.X + (screenSize.X - contentWidth) / 2;
                top = screenTopLeft.Y + 24;
                break;
            case OverlayPosition.FixedBottomCenterOfScreen:
                left = screenTopLeft.X + (screenSize.X - contentWidth) / 2;
                top = screenTopLeft.Y + screenSize.Y - contentHeight - 48;
                break;
            case OverlayPosition.BelowCaptureArea:
            default:
                left = regionBottomLeft.X + (regionSize.X - contentWidth) / 2;
                top = regionBottomLeft.Y + 8;
                break;
        }

        // Keep the whole window on-screen: a small or edge-of-screen region (as used by the
        // "translate once" hotkey, or a capture area near a corner) could otherwise push part
        // of the overlay past the screen edge, especially once translated text is wider/taller
        // than the original region.
        var maxLeft = screenTopLeft.X + Math.Max(0, screenSize.X - contentWidth);
        var maxTop = screenTopLeft.Y + Math.Max(0, screenSize.Y - contentHeight);
        left = Math.Clamp(left, screenTopLeft.X, Math.Max(screenTopLeft.X, maxLeft));
        top = Math.Clamp(top, screenTopLeft.Y, Math.Max(screenTopLeft.Y, maxTop));

        Left = left;
        Top = top;
    }
}
