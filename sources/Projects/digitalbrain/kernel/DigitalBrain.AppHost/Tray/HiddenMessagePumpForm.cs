namespace DigitalBrain.Hosting.Tray;

// Invisible Form whose only job is to give Win32 an HWND that can receive
// WM_HOTKEY (0x0312) and the named-pipe / URL-scheme dispatch we forward
// via SendMessage. Never visible to the user.
internal sealed class HiddenMessagePumpForm : Form
{
    public const int WM_HOTKEY = 0x0312;

    public event Action<int>? HotkeyPressed;

    public HiddenMessagePumpForm()
    {
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
        FormBorderStyle = FormBorderStyle.None;
        Opacity = 0;
        Width = 0;
        Height = 0;
    }

    protected override void SetVisibleCore(bool value)
    {
        // Force the HWND to be created without the form ever becoming visible.
        if (!IsHandleCreated) CreateHandle();
        base.SetVisibleCore(false);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(m.WParam.ToInt32());
            return;
        }
        base.WndProc(ref m);
    }
}
