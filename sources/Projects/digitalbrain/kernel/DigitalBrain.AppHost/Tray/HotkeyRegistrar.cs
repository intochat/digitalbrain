using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Hosting.Tray;

// Win32 RegisterHotKey + WM_HOTKEY: the only sanctioned way to define a
// system-wide keyboard shortcut on Windows. The pump form's WndProc raises
// HotkeyPressed with the wParam (hotkey id); this class maps id -> Action.
internal sealed class HotkeyRegistrar : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [Flags]
    public enum Modifiers : uint
    {
        None = 0,
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
        NoRepeat = 0x4000,
    }

    public const int ShowBrainSceneHotkeyId = 1;

    private readonly ILogger _logger;
    private readonly Dictionary<int, Action> _handlers = new();
    private IntPtr _ownerHwnd;

    public HotkeyRegistrar(ILogger logger)
    {
        _logger = logger;
    }

    public bool Register(IntPtr ownerHwnd, int id, Modifiers modifiers, Keys virtualKey, Action onPressed)
    {
        _ownerHwnd = ownerHwnd;
        _handlers[id] = onPressed;
        var ok = RegisterHotKey(ownerHwnd, id, (uint)(modifiers | Modifiers.NoRepeat), (uint)virtualKey);
        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            _logger.LogWarning(
                "RegisterHotKey({Modifiers}+{Key}) failed with Win32 error {Error}. " +
                "The combination is likely claimed by another process.",
                modifiers, virtualKey, err);
        }
        return ok;
    }

    public void OnHotkey(int hotkeyId)
    {
        if (_handlers.TryGetValue(hotkeyId, out var handler))
        {
            try { handler(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hotkey handler for id {Id} threw.", hotkeyId);
            }
        }
    }

    public void Dispose()
    {
        if (_ownerHwnd == IntPtr.Zero) return;
        foreach (var id in _handlers.Keys)
            UnregisterHotKey(_ownerHwnd, id);
        _handlers.Clear();
        _ownerHwnd = IntPtr.Zero;
    }
}
