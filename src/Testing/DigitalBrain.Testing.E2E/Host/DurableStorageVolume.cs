using System.ComponentModel;
using System.Diagnostics;

namespace DigitalBrain.Testing.E2E;

// A named Docker volume that outlives the host process which created it, so a killed-and-restarted
// host mounts the same cluster storage. Every host of one test passes the same key; the test process
// removes the volume when it exits.
internal static class DurableStorageVolume
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Volumes = new(StringComparer.Ordinal);
    private static bool _registered;

    internal static string Acquire(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var sanitized = new string([.. key.Select(character => char.IsLetterOrDigit(character) || character is '_' or '.' or '-' ? character : '-')]);
        var name = "brain-e2e-" + sanitized;
        lock (Gate)
        {
            Volumes.Add(name);
            if (!_registered)
            {
                _registered = true;
                AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveAll();
            }
        }
        return name;
    }

    private static void RemoveAll()
    {
        string[] names;
        lock (Gate) { names = [.. Volumes]; }
        foreach (var name in names)
        {
            foreach (var container in Split(Run("ps", "-aq", "--filter", "volume=" + name)))
            { Run("rm", "-f", container); }
            Run("volume", "rm", "-f", name);
        }
    }

    private static string[] Split(string output)
        => output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string Run(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("docker") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in arguments) { startInfo.ArgumentList.Add(argument); }
            using var process = Process.Start(startInfo);
            if (process is null) { return ""; }
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output;
        }
        catch (Win32Exception) { return ""; }
        catch (InvalidOperationException) { return ""; }
    }
}
