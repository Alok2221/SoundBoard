using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace SoundboardApp.Services;

public sealed class HotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000; // Windows Vista+ - no key-repeat spam

    private readonly Dictionary<int, Action> _callbacks = new();
    private readonly Dictionary<string, int> _registeredByGesture = new();
    private HwndSource? _source;
    private IntPtr _hwnd = IntPtr.Zero;
    private int _nextId = 1;
    private bool _disposed;

    public void Attach(Window window)
    {
        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        _hwnd = helper.Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    public bool Register(string gesture, Action callback)
    {
        if (_disposed || _hwnd == IntPtr.Zero || string.IsNullOrWhiteSpace(gesture))
            return false;

        Unregister(gesture);

        if (!TryParseGesture(gesture, out var modifiers, out var key))
            return false;

        if (!IsAllowedGlobalHotkey(modifiers, key))
            return false;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
            return false;

        var id = _nextId++;
        var fsModifiers = modifiers | ModNoRepeat;

        if (!RegisterHotKey(_hwnd, id, fsModifiers, (uint)vk))
            return false;

        _callbacks[id] = callback;
        _registeredByGesture[Normalize(gesture)] = id;
        return true;
    }

    public void Unregister(string? gesture)
    {
        if (string.IsNullOrWhiteSpace(gesture) || _hwnd == IntPtr.Zero)
            return;

        var key = Normalize(gesture);
        if (!_registeredByGesture.TryGetValue(key, out var id))
            return;

        UnregisterHotKey(_hwnd, id);
        _registeredByGesture.Remove(key);
        _callbacks.Remove(id);
    }

    public void UnregisterAll()
    {
        if (_hwnd != IntPtr.Zero)
        {
            foreach (var id in _registeredByGesture.Values.ToList())
                UnregisterHotKey(_hwnd, id);
        }

        _registeredByGesture.Clear();
        _callbacks.Clear();
    }

    /// <summary>
    /// Global hotkeys without a modifier are limited to F1–F24 -
    /// bare letters/digits would steal typing from other apps.
    /// </summary>
    public static bool IsAllowedGlobalHotkey(uint modifiers, Key key)
    {
        var hasModifier = (modifiers & (ModAlt | ModControl | ModShift | ModWin)) != 0;
        if (hasModifier)
            return key is not (Key.None or Key.Escape);

        return key is >= Key.F1 and <= Key.F24;
    }

    public static bool IsAllowedGlobalHotkey(ModifierKeys modifiers, Key key)
    {
        uint mods = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModAlt;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= ModControl;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModShift;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModWin;
        return IsAllowedGlobalHotkey(mods, key);
    }

    public static string FormatGesture(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    public static bool TryParseGesture(string gesture, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;

        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            modifiers |= parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "windows" => ModWin,
                _ => 0u
            };
        }

        return Enum.TryParse(parts[^1], ignoreCase: true, out key) && key != Key.None;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmHotkey)
            return IntPtr.Zero;

        var id = wParam.ToInt32();
        if (!_callbacks.TryGetValue(id, out var callback))
            return IntPtr.Zero;

        try
        {
            callback();
        }
        catch
        {
            // never let a hotkey callback kill the message loop
        }

        handled = true;
        return IntPtr.Zero;
    }

    private static string Normalize(string gesture) =>
        string.Join("+", gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Trim()));

    public void Dispose()
    {
        if (_disposed)
            return;

        UnregisterAll();
        _source?.RemoveHook(WndProc);
        _source = null;
        _hwnd = IntPtr.Zero;
        _disposed = true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
