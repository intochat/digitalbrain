using System.Diagnostics;
using Xunit;

namespace DigitalBrain.ProductTests;

internal static class LiveProductAspire
{
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);
    internal const string AppHostPath = "os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj";
    internal const string McpResource = "digitalbrain-mcp";

    internal static async Task<CommandResult> RunAsync(
        string repository,
        bool allowFailure,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var commandTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandTimeout.CancelAfter(CommandTimeout);

        var start = new ProcessStartInfo("aspire")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new Xunit.Sdk.XunitException("The Aspire CLI process did not start.");
        }

        var standardOutput = process.StandardOutput.ReadToEndAsync(commandTimeout.Token);
        var standardError = process.StandardError.ReadToEndAsync(commandTimeout.Token);

        try
        {
            await process.WaitForExitAsync(commandTimeout.Token);
        }
        catch
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        var result = new CommandResult(
            process.ExitCode,
            await standardOutput,
            await standardError);

        if (!allowFailure && result.ExitCode != 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"aspire {string.Join(' ', arguments)} exited with {result.ExitCode}.{Environment.NewLine}"
                + result.StandardOutput
                + Environment.NewLine
                + result.StandardError);
        }

        return result;
    }

    internal static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new Xunit.Sdk.XunitException(
                $"Could not find DigitalBrain.slnx above {AppContext.BaseDirectory}.");
    }

    internal sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
