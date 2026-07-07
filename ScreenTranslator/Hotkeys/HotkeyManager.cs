using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace ScreenTranslator.Hotkeys;

/// <summary>
/// Registers and dispatches global (system-wide) hotkeys via the classic Win32
/// RegisterHotKey/WM_HOTKEY mechanism. This works even while a game has input focus and is
/// running in exclusive/borderless/windowed mode, because it's handled by the OS, not by
/// window messages routed through the game.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        // A hidden, message-only-ish window purely to receive WM_HOTKEY. Zero size, never shown.
        var parameters = new HwndSourceParameters("ScreenTranslatorHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a hotkey from a combo string like "Ctrl+Shift+A". Returns false if the combo is already taken system-wide.</summary>
    public bool Register(string combo, Action action)
    {
        if (!TryParse(combo, out var modifiers, out var key)) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, vk)) return false;
        _handlers[id] = action;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList())
            UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public static bool TryParse(string combo, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(combo)) return false;

        var parts = combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": case "windows": modifiers |= MOD_WIN; break;
                default: return false;
            }
        }

        return Enum.TryParse(parts[^1], true, out key);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
