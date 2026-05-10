using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.System;

namespace WallpaperManager.Services;

public sealed class GlobalHotkeyService : IDisposable
{
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSW
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int ptX, ptY; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassW(ref WNDCLASSW c);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetMessageW(out MSG m, IntPtr h, uint f, uint t);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref MSG m);
    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr h, int id);

    private const int  HOTKEY_ID       = 9001;
    private const uint MOD_ALT         = 0x0001;
    private const uint MOD_CONTROL     = 0x0002;
    private const uint MOD_SHIFT       = 0x0004;
    private const uint MOD_NOREPEAT    = 0x4000;
    private const uint WM_HOTKEY       = 0x0312;
    private const uint WM_QUIT         = 0x0012;
    private const uint WM_APP_REGISTER = 0x8001;
    private const uint WM_APP_UNREG    = 0x8002;

    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private IntPtr            _hwnd;
    private WndProcDelegate?  _wndProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private bool _disposed;

    public event EventHandler? HotkeyPressed;

    public GlobalHotkeyService()
    {
        var t = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyThread" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        _ready.Wait(3000);
    }

    // ── Public API ─────────────────────────────────────────────────────

    public void RegisterShortcut(string shortcut)
    {
        if (!_ready.IsSet) _ready.Wait(3000);

        // Always unregister first (from the dedicated thread)
        PostMessageW(_hwnd, WM_APP_UNREG, IntPtr.Zero, IntPtr.Zero);

        if (string.IsNullOrWhiteSpace(shortcut) || shortcut == "None" || _hwnd == IntPtr.Zero) return;

        uint mods = MOD_NOREPEAT;
        if (shortcut.Contains("Ctrl",  StringComparison.OrdinalIgnoreCase)) mods |= MOD_CONTROL;
        if (shortcut.Contains("Shift", StringComparison.OrdinalIgnoreCase)) mods |= MOD_SHIFT;
        if (shortcut.Contains("Alt",   StringComparison.OrdinalIgnoreCase)) mods |= MOD_ALT;

        var keyStr = shortcut.Split('+')[^1].Trim();
        if (Enum.TryParse<VirtualKey>(keyStr, ignoreCase: true, out var vk))
        {
            // Post registration to the dedicated thread — RegisterHotKey MUST be called
            // from the thread whose queue will receive WM_HOTKEY.
            PostMessageW(_hwnd, WM_APP_REGISTER, (IntPtr)(long)mods, (IntPtr)(long)(uint)vk);
            Debug.WriteLine($"[Hotkey] Register request posted: '{shortcut}' vk={vk}({(uint)vk}) mods=0x{mods:X}");
        }
        else
        {
            Debug.WriteLine($"[Hotkey] Cannot parse key '{keyStr}' from '{shortcut}'.");
        }
    }

    public void Unregister()
    {
        if (_hwnd != IntPtr.Zero)
            PostMessageW(_hwnd, WM_APP_UNREG, IntPtr.Zero, IntPtr.Zero);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != IntPtr.Zero) PostMessageW(_hwnd, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _ready.Dispose();
    }

    // ── Dedicated STA message thread ───────────────────────────────────

    private void MessageLoop()
    {
        var className = $"CarbonHotkeyWnd_{Environment.ProcessId}";
        _wndProc = WndProc;
        var wc = new WNDCLASSW { lpfnWndProc = _wndProc, lpszClassName = className };
        RegisterClassW(ref wc);

        _hwnd = CreateWindowExW(0, className, "CarbonHotkeyWindow", 0, 0, 0, 0, 0,
            HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        Debug.WriteLine(_hwnd == IntPtr.Zero
            ? $"[Hotkey] Window creation FAILED. Error: {Marshal.GetLastWin32Error()}"
            : $"[Hotkey] Window ready: 0x{_hwnd:X}");

        _ready.Set();

        while (GetMessageW(out MSG msg, IntPtr.Zero, 0, 0))
        {
            // WM_HOTKEY is a *thread* message (hwnd=null); DispatchMessage ignores it.
            // Handle it directly here.
            if (msg.message == WM_HOTKEY && msg.wParam.ToInt32() == HOTKEY_ID)
            {
                Debug.WriteLine("[Hotkey] WM_HOTKEY received!");
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
            }
            else if (msg.message == WM_APP_REGISTER)
            {
                uint mods = (uint)(long)msg.wParam;
                uint vk   = (uint)(long)msg.lParam;
                bool ok   = RegisterHotKey(_hwnd, HOTKEY_ID, mods, vk);
                Debug.WriteLine(ok
                    ? $"[Hotkey] RegisterHotKey OK vk={vk} mods=0x{mods:X}"
                    : $"[Hotkey] RegisterHotKey FAILED error={Marshal.GetLastWin32Error()}");
            }
            else if (msg.message == WM_APP_UNREG)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                Debug.WriteLine("[Hotkey] UnregisterHotKey called.");
            }
            else
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
        }
    }

    private IntPtr WndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
        => DefWindowProcW(hWnd, uMsg, wParam, lParam);
}
