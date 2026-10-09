using System;
using System.Runtime.InteropServices;

namespace ScreenTranslator.Hotkeys;

/// <summary>
/// Fires on every Space key press anywhere on screen, without ever swallowing it - whatever has
/// focus (game, desktop, etc.) still receives it exactly as if this weren't running. Sibling to
/// <see cref="LeftClickWatcher"/>: both are ways to dismiss a one-off/hover-translate overlay
/// early, since that overlay has no close button (it's click-through) and would otherwise sit on
/// screen - directly over the original text - until its auto-hide timer runs out.
/// </summary>
public sealed class DismissKeyWatcher : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const uint VK_SPACE = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Raised on every Space key-down, anywhere on screen, while the watcher is started.</summary>
    public event Action? Pressed;

    private readonly LowLevelKeyboardProc _proc; // kept as a field - must not be GC'd while the hook is installed
    private IntPtr _hookHandle = IntPtr.Zero;

    public DismissKeyWatcher()
    {
        _proc = HookCallback;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_KEYDOWN)
        {
            var vk = (uint)Marshal.ReadInt32(lParam); // vkCode is the struct's first field
            if (vk == VK_SPACE)
                Pressed?.Invoke();
        }

        // Never swallow the key - always pass it through to the next hook / whatever has focus.
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
