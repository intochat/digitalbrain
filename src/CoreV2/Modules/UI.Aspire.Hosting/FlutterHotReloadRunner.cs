using System.Diagnostics;
using System.Text;

namespace Brain.Modules.UI.Aspire.Hosting;

internal static class FlutterHotReloadRunner
{
    internal static async Task ReloadAsync(
        string flutterCommand,
        string workingDirectory,
        string deviceTarget,
        int ddsPort,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = new Process
        {
            StartInfo = CreateStartInfo(flutterCommand, workingDirectory, deviceTarget, ddsPort),
        };
        var output = new StringBuilder();
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Flutter attach did not start.");
            }

            var stderr = DrainAsync(process.StandardError, output, timeout.Token);
            var reloadSent = false;
            while (await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                output.AppendLine(line);
                if (!reloadSent && line.Contains("Flutter run key commands", StringComparison.Ordinal))
                {
                    await process.StandardInput.WriteLineAsync("r");
                    await process.StandardInput.FlushAsync(timeout.Token);
                    reloadSent = true;
                }

                if (line.Contains("Reloaded ", StringComparison.Ordinal))
                {
                    return;
                }

                if (line.Contains("Hot reload failed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(line);
                }
            }

            await stderr;
            throw new InvalidOperationException(
                $"Flutter attach exited before hot reload completed.{Environment.NewLine}{output}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Flutter attach did not complete hot reload within 45 seconds.{Environment.NewLine}{output}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(
        string flutterCommand,
        string workingDirectory,
        string deviceTarget,
        int ddsPort)
    {
        var command = flutterCommand;
        if (OperatingSystem.IsWindows()
            && !Path.HasExtension(command)
            && File.Exists(command + ".bat"))
        {
            command += ".bat";
        }

        var info = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : command,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (OperatingSystem.IsWindows())
        {
            info.ArgumentList.Add("/d");
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add(command);
        }

        info.ArgumentList.Add("attach");
        info.ArgumentList.Add("-d");
        info.ArgumentList.Add(deviceTarget);
        info.ArgumentList.Add("--debug-url");
        info.ArgumentList.Add($"http://127.0.0.1:{ddsPort}");
        info.ArgumentList.Add("--no-dds");
        return info;
    }

    private static async Task DrainAsync(
        StreamReader reader,
        StringBuilder output,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            output.AppendLine(line);
        }
    }
}
