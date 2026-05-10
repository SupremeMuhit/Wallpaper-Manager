using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.System;

namespace WallpaperManager.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private IntPtr _hWnd;
    private bool _isRegistered;

    public event EventHandler? HotkeyPressed;

    public void Initialize(IntPtr hWnd)
    {
        _hWnd = hWnd;
    }

    public void RegisterShortcut(string shortcut)
    {
        Unregister();
        if (string.IsNullOrWhiteSpace(shortcut) || shortcut == "None" || _hWnd == IntPtr.Zero) return;

        try
        {
            uint modifiers = 0;
            if (shortcut.Contains("Ctrl", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_CONTROL;
            if (shortcut.Contains("Shift", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_SHIFT;
            if (shortcut.Contains("Alt", StringComparison.OrdinalIgnoreCase)) modifiers |= MOD_ALT;

            var parts = shortcut.Split('+');
            if (parts.Length > 0)
            {
                var keyStr = parts[^1].Trim();
                if (Enum.TryParse<VirtualKey>(keyStr, true, out var key))
                {
                    bool success = RegisterHotKey(_hWnd, HOTKEY_ID, modifiers, (uint)key);
                    if (success)
                    {
                        _isRegistered = true;
                        Debug.WriteLine($"Hotkey registered successfully: {shortcut}");
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to register hotkey. Error: {Marshal.GetLastWin32Error()}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error registering hotkey: {ex.Message}");
        }
    }

    public void Unregister()
    {
        if (_isRegistered && _hWnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hWnd, HOTKEY_ID);
            _isRegistered = false;
            Debug.WriteLine("Hotkey unregistered.");
        }
    }

    public void HandleMessage(uint msg, IntPtr wParam)
    {
        try
        {
            const uint WM_HOTKEY = 0x0312;
            if (msg == WM_HOTKEY && (long)wParam == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error handling hotkey message: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Unregister();
    }
}
