using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Point = System.Windows.Point;
using ScreenTranslator.Localization;

namespace ScreenTranslator.Capture;

/// <summary>
/// Fullscreen (spanning the whole virtual desktop) transparent window used by the "confirm click"
/// instant-translate mode: shows a visible cursor and waits for a single click, instead of
/// translating wherever the real cursor already is. Meant for games that hide/replace the mouse
/// cursor, where the player otherwise has no way to see where "the cursor" actually is before
/// triggering a translate. Mirrors RegionSelectorWindow's window setup and DPI handling, but
/// returns one point instead of a dragged rectangle.
/// </summary>
public partial class ClickPointSelectorWindow : Window
{
    [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr lpRect);
    [DllImport("user32.dll")] private static extern int ShowCursor(bool bShow);

    public Point? SelectedPoint { get; private set; }

    private readonly System.Drawing.Rectangle _virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;

    public ClickPointSelectorWindow()
    {
        InitializeComponent();
        HintText.Text = Loc.S("HoverConfirm.Hint");

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

            // Same reasoning as RegionSelectorWindow: release any cursor clip/hide a game's
            // mouse-look might have left in effect, so the player can actually see and use this.
            try
            {
                ClipCursor(IntPtr.Zero);
                ShowCursor(true);
            }
            catch { /* best effort */ }
        };

        MouseLeftButtonDown += OnMouseLeftButtonDown;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { SelectedPoint = null; Close(); } };
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(RootCanvas);

        // Same DIP -> physical-pixel conversion RegionSelectorWindow uses, so this lines up
        // correctly under per-monitor DPI scaling.
        var src = PresentationSource.FromVisual(this);
        var m = src?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
        var device = m.Transform(p);

        SelectedPoint = new Point(_virtualScreen.Left + device.X, _virtualScreen.Top + device.Y);
        Close();
    }
}
