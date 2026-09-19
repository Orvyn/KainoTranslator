using System;
using System.Runtime.InteropServices;

namespace ScreenTranslator.Hotkeys;

/// <summary>
/// Fires on every left mouse button click anywhere on screen, without ever swallowing the click -
/// whatever's under the cursor (game, desktop, etc.) still receives it exactly as if this weren't
/// running. Used to dismiss a one-off/hover-translate overlay on the next click: that overlay has
/// no close button (it's click-through) and would otherwise sit on screen - directly over the
/// original text - until its auto-hide timer runs out, which gets in the way of capturing the
/// *next* line of dialogue in the same spot.
/// </summary>
public sealed class LeftClickWatcher : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Raised on left-button-down, anywhere on screen, while the watcher is started.</summary>
    public event Action? Clicked;

    private readonly LowLevelMouseProc _proc; // kept as a field - must not be GC'd while the hook is installed
    private IntPtr _hookHandle = IntPtr.Zero;

    public LeftClickWatcher()
    {
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
        if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
            Clicked?.Invoke();

        // Never swallow the click - always pass it through to the next hook / whatever's under the cursor.
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
