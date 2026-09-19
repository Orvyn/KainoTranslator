using System;
using System.Runtime.InteropServices;

namespace ScreenTranslator.Hotkeys;

/// <summary>Mouse buttons instant-translate can be bound to. Deliberately excludes Left - that
/// button is reserved for normal clicking (including dismissing the instant-translate overlay
/// itself - see LeftClickWatcher), so binding it here would be a self-conflict.</summary>
public enum HoverMouseButton { Right, Middle, XButton1, XButton2 }

/// <summary>
/// Fires when the configured mouse button goes down, anywhere on screen, without ever swallowing
/// the click - same non-intrusive philosophy as LeftClickWatcher and BareKeyHook. Unlike
/// BareKeyHook's "tap with nothing else held" logic, this fires on plain button-down: mouse
/// buttons don't have the same "held together with movement" combo pattern that keyboard
/// modifiers do, so the simpler, more immediate trigger fits how a mouse button press is
/// normally used.
/// </summary>
public sealed class BareMouseButtonHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int XBUTTON1 = 0x0001;
    private const int XBUTTON2 = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    public event Action? Tapped;

    private readonly LowLevelMouseProc _proc; // kept as a field - must not be GC'd while the hook is installed
    private readonly HoverMouseButton _button;
    private IntPtr _hookHandle = IntPtr.Zero;

    public BareMouseButtonHook(HoverMouseButton button)
    {
        _button = button;
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && IsMatch(wParam.ToInt32(), lParam))
            Tapped?.Invoke();

        // Never swallow the click - always pass it through to the next hook / whatever's under the cursor.
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private bool IsMatch(int msg, IntPtr lParam) => _button switch
    {
        HoverMouseButton.Right => msg == WM_RBUTTONDOWN,
        HoverMouseButton.Middle => msg == WM_MBUTTONDOWN,
        HoverMouseButton.XButton1 => msg == WM_XBUTTONDOWN && GetXButtonId(lParam) == XBUTTON1,
        HoverMouseButton.XButton2 => msg == WM_XBUTTONDOWN && GetXButtonId(lParam) == XBUTTON2,
        _ => false
    };

    private static int GetXButtonId(IntPtr lParam)
    {
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        return (int)(data.MouseData >> 16); // high word of mouseData distinguishes XBUTTON1/XBUTTON2
    }

    public void Dispose() => Stop();
}
