using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Hosting.Tray;

// v6 V6-1: WinForms NotifyIcon hosted alongside the Aspire
// DistributedApplication. Owns the icon and a dedicated STA message
// pump; defers silo lifecycle to IHostApplicationLifetime so the
// "Quit" menu item triggers a clean shutdown of the AppHost (and with
// it every Aspire-managed resource).
internal sealed class TrayDaemon : IHostedService, IDisposable
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<TrayDaemon> _logger;
    private readonly NotifyIcon _icon = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ManualResetEventSlim _pumpReady = new(false);
    private readonly HotkeyRegistrar _hotkeys;
    private readonly SingleInstancePipe _pipe;
    private Thread? _pumpThread;
    private HiddenMessagePumpForm? _pumpForm;

    public TrayDaemon(
        IHostApplicationLifetime lifetime,
        ILogger<TrayDaemon> logger,
        ILoggerFactory loggerFactory)
    {
        _lifetime = lifetime;
        _logger = logger;
        _hotkeys = new HotkeyRegistrar(logger);
        var urlHandler = new UrlSchemeHandler(loggerFactory.CreateLogger<UrlSchemeHandler>());
        _pipe = new SingleInstancePipe(loggerFactory.CreateLogger<SingleInstancePipe>(), urlHandler);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureAutostartInstalled();
        _pipe.TryAcquireServer();
        _pumpThread = new Thread(RunPump)
        {
            IsBackground = true,
            Name = "digitalbrain-tray-pump",
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
        _pumpReady.Wait(cancellationToken);
        _logger.LogInformation("Tray daemon started.");
        return Task.CompletedTask;
    }

    private void EnsureAutostartInstalled()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;
            AutostartBootstrap.EnsureInstalled(exePath, _logger);
        }
        catch (Exception ex)
        {
            // Non-fatal: tray still works without autostart. User can
            // re-enable from Preferences once V6-2 lands the settings UI.
            _logger.LogWarning(ex, "Failed to repair HKCU autostart entry.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_pumpForm is { IsHandleCreated: true } form)
            {
                form.BeginInvoke(() =>
                {
                    _icon.Visible = false;
                    Application.ExitThread();
                });
            }
            _pumpThread?.Join(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tray daemon stop encountered an error.");
        }
        return Task.CompletedTask;
    }

    private void RunPump()
    {
        try
        {
            _pumpForm = new HiddenMessagePumpForm();
            _pumpForm.HotkeyPressed += _hotkeys.OnHotkey;
            _ = _pumpForm.Handle; // realise the HWND

            _icon.Icon = LoadTrayIcon();
            _icon.Text = "DigitalBrain — Primary Cortex";
            _icon.Visible = true;
            _icon.ContextMenuStrip = BuildMenu();
            _icon.DoubleClick += (_, _) => OnShowBrainScene();

            RegisterDefaultHotkey();

            _pumpReady.Set();
            Application.Run(_pumpForm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray pump crashed.");
            _pumpReady.Set();
            _lifetime.StopApplication();
        }
        finally
        {
            _icon.Visible = false;
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        _menu.Items.Add(new ToolStripLabel("DigitalBrain") { Enabled = false });
        _menu.Items.Add(new ToolStripSeparator());
        var showScene = new ToolStripMenuItem("Show Brain Scene", image: null, (_, _) => OnShowBrainScene());
        showScene.ShortcutKeyDisplayString = "Ctrl+'";
        _menu.Items.Add(showScene);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Quit", image: null, (_, _) => OnQuit()));
        return _menu;
    }

    private void OnShowBrainScene()
    {
        // V6-2 wires this to the gRPC ShowBrainSceneRequest; the V6-1
        // scaffolding only needs the menu wiring to work and to log.
        _logger.LogInformation("Tray: Show Brain Scene requested.");
    }

    private void RegisterDefaultHotkey()
    {
        // Ctrl+' (VK_OEM_7 = 0xDE). Picked because Win+Space / Ctrl+Space /
        // Alt+Space are all reserved by Windows or IDEs; Ctrl+' is unclaimed.
        var ok = _hotkeys.Register(
            _pumpForm!.Handle,
            HotkeyRegistrar.ShowBrainSceneHotkeyId,
            HotkeyRegistrar.Modifiers.Control,
            Keys.OemQuotes,
            OnShowBrainScene);
        if (!ok)
        {
            _icon.ShowBalloonTip(
                timeout: 5000,
                tipTitle: "Hotkey unavailable",
                tipText: "Ctrl+' is already claimed by another app. Rebind in Preferences.",
                tipIcon: ToolTipIcon.Warning);
        }
    }

    private void OnQuit()
    {
        _logger.LogInformation("Tray: Quit requested.");
        _lifetime.StopApplication();
    }

    private static Icon LoadTrayIcon()
    {
        // V6-1 scaffolding: ship with the system Application icon until the
        // packaged digitalbrain.ico lands with the MSIX installer.
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
        _hotkeys.Dispose();
        _pipe.Dispose();
        _pumpForm?.Dispose();
        _pumpReady.Dispose();
    }
}
