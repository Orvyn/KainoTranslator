using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ScreenTranslator.Hotkeys;

/// <summary>
/// Fires when a single key is pressed and released on its own - no other key going down while
/// it's held - without ever swallowing the key itself (games/other apps still see every
/// keystroke normally). Used for the "hover + tap Shift" one-shot translate mode: RegisterHotKey
/// (see <see cref="HotkeyManager"/>) can't represent a bare modifier key with no companion key,
/// so this uses a low-level keyboard hook (WH_KEYBOARD_LL) instead, which is the same mechanism
/// Windows itself uses to detect a bare Shift tap (e.g. for the "switch keyboard layout" option).
///
/// Caveat: a tap is still delivered to whatever app/game has focus like any other keypress, so if
/// the trigger key doubles as an in-game action (Shift = sprint is common), that action will also
/// fire briefly. Pick a trigger key that's unlikely to collide, or make it configurable.
/// </summary>
public sealed class BareKeyHook : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>Raised after the trigger key was pressed and released with no other key in between.</summary>
    public event Action? Tapped;

    private readonly LowLevelKeyboardProc _proc; // kept as a field - must not be GC'd while the hook is installed
    private IntPtr _hookHandle = IntPtr.Zero;
    private readonly uint[] _triggerVkCodes;
    private bool _armed;
    private bool _disqualified;

    /// <param name="triggerKey">The key to watch for (e.g. Key.LeftShift). Left/right variants of
    /// Shift/Ctrl/Alt are automatically matched to either side, since Windows can report either
    /// the generic or the side-specific virtual-key code depending on the keyboard driver.</param>
    public BareKeyHook(Key triggerKey)
    {
        _triggerVkCodes = ResolveVkCodes(triggerKey);
        _proc = HookCallback; // pin the delegate
    }

    private static uint[] ResolveVkCodes(Key key) => key switch
    {
        Key.LeftShift or Key.RightShift => new uint[] { 0x10, 0xA0, 0xA1 },   // VK_SHIFT, VK_LSHIFT, VK_RSHIFT
        Key.LeftCtrl or Key.RightCtrl => new uint[] { 0x11, 0xA2, 0xA3 },     // VK_CONTROL, VK_LCONTROL, VK_RCONTROL
        Key.LeftAlt or Key.RightAlt => new uint[] { 0x12, 0xA4, 0xA5 },       // VK_MENU, VK_LMENU, VK_RMENU
        Key.CapsLock => new uint[] { 0x14 },
        _ => new uint[] { (uint)KeyInterop.VirtualKeyFromKey(key) }
    };

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
        _armed = false;
        _disqualified = false;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            var vk = (uint)Marshal.ReadInt32(lParam); // vkCode is the struct's first field

            var isTriggerKey = Array.IndexOf(_triggerVkCodes, vk) >= 0;

            if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
            {
                if (isTriggerKey)
                {
                    if (!_armed) { _armed = true; _disqualified = false; }
                    // repeat-fire while held is ignored (auto-repeat), tap only fires on release
                }
                else if (_armed)
                {
                    // Some other key went down while the trigger was held - this is a combo
                    // (e.g. Shift+A), not a bare tap, so disqualify it.
                    _disqualified = true;
                }
            }
            else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && isTriggerKey)
            {
                if (_armed && !_disqualified)
                    Tapped?.Invoke();
                _armed = false;
                _disqualified = false;
            }
        }

        // Never swallow the key - always pass it through to the next hook / the focused app.
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
