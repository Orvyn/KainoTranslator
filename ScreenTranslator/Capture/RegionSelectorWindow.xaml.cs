using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using ScreenTranslator.Localization;

namespace ScreenTranslator.Capture;

/// <summary>
/// Fullscreen (spanning the whole virtual desktop) transparent window used to let the user
/// drag out the rectangle they want captured/translated. Returns the result in physical
/// (device) pixels so it lines up correctly regardless of per-monitor DPI scaling.
/// </summary>
public partial class RegionSelectorWindow : Window
{
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
    [DllImport("user32.dll")] private static extern int ShowCursor(bool bShow);

    public Rectangle? SelectedRegion { get; private set; }

    private Point _start;
    private bool _dragging;

    // Virtual screen bounds in physical pixels (System.Windows.Forms gives us physical pixels here).
    private readonly System.Drawing.Rectangle _virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        HintText.Text = Loc.S("Region.Hint");

        // Position/size the window in WPF (DIP) units. Because different monitors can have
        // different DPI, we can't just divide by a single scale factor - so instead we place
        // this window using the primitive Win32 SetWindowPos-equivalent WPF exposes via
        // WindowStartupLocation=Manual + Left/Top/Width/Height set in the Loaded handler using
        // the *primary monitor's* WPF->device scale as an approximation, then rely on
        // PerMonitorV2 + WPF's automatic per-monitor DPI handling to keep the canvas itself
        // pixel-accurate through the ScreenToPhysical conversion in OnMouseUp.
        Loaded += (_, _) =>
        {
            var src = PresentationSource.FromVisual(this);
            var m = src?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topLeft = m.Transform(new Point(_virtualScreen.Left, _virtualScreen.Top));
            var size = m.Transform(new Point(_virtualScreen.Width, _virtualScreen.Height));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = size.X;
            Height = size.Y;

            // Some games with mouse-look (FPS/3D camera control) confine the cursor to the
            // window (or its center) via ClipCursor and hide it via ShowCursor - if that's still
            // in effect when this selector appears, the cursor can look "stuck" in the middle of
            // the screen. Release both so the cursor is free and visible for the selection.
            try
            {
                ClipCursor(IntPtr.Zero);
                ShowCursor(true);
            }
            catch { /* best effort - never worth failing region selection over */ }
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { SelectedRegion = null; Close(); } };
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        _dragging = true;
        HintText.Visibility = Visibility.Collapsed;
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, p.X);
        var y = Math.Min(_start.Y, p.Y);
        var w = Math.Abs(p.X - _start.X);
        var h = Math.Abs(p.Y - _start.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var end = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, end.X);
        var y = Math.Min(_start.Y, end.Y);
        var w = Math.Abs(end.X - _start.X);
        var h = Math.Abs(end.Y - _start.Y);

        if (w < 5 || h < 5) { SelectedRegion = null; Close(); return; }

        // Convert this window's DIP-space rectangle back to physical pixels using the
        // per-monitor transform active for this window, then offset by the virtual screen
        // origin (which itself is already in physical pixels).
        var src = PresentationSource.FromVisual(this);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var topLeftDevice = m.Transform(new Point(x, y));
        var sizeDevice = m.Transform(new Point(w, h));

        SelectedRegion = new Rectangle(
            _virtualScreen.Left + (int)Math.Round(topLeftDevice.X),
            _virtualScreen.Top + (int)Math.Round(topLeftDevice.Y),
            (int)Math.Round(sizeDevice.X),
            (int)Math.Round(sizeDevice.Y));

        Close();
    }
}
