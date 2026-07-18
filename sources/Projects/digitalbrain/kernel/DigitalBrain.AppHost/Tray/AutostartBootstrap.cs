using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace DigitalBrain.Hosting.Tray;

// Writes / repairs / removes the HKCU autostart entry per
// docs/final-simplification/02-WINDOWS-AUTOSTART.md section 2.
//
// HKCU (not HKLM) is the decision per docs/final-simplification/
// 10-RISKS-AND-DECISIONS.md decision-log row 1: no UAC, DPAPI keys are
// user-scoped, and the user can disable from Task Manager -> Startup.
internal static class AutostartBootstrap
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DigitalBrain";

    // Idempotent: writes the Run value only if the expected command differs
    // from the current one (or no value is set yet).
    public static void EnsureInstalled(string exePath, ILogger logger)
    {
        var expected = $"\"{exePath}\" --profile=product --tray";
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                       ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                       ?? throw new InvalidOperationException(
                            $"Could not open or create HKCU\\{RunKeyPath}");
        var actual = run.GetValue(ValueName) as string;
        if (string.Equals(expected, actual, StringComparison.Ordinal)) return;

        run.SetValue(ValueName, expected, RegistryValueKind.String);
        logger.LogInformation(
            "Wrote HKCU\\{Key}\\{Value} -> {Command}",
            RunKeyPath, ValueName, expected);
    }

    public static void EnsureUninstalled(ILogger logger)
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (run is null) return;
        if (run.GetValue(ValueName) is null) return;
        run.DeleteValue(ValueName, throwOnMissingValue: false);
        logger.LogInformation("Removed HKCU\\{Key}\\{Value}.", RunKeyPath, ValueName);
    }

    public static string? CurrentInstalledCommand()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return run?.GetValue(ValueName) as string;
    }
}
