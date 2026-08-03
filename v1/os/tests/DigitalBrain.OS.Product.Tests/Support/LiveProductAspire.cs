using System.Diagnostics;
using System.Text.Json.Nodes;
using Xunit;

namespace DigitalBrain.ProductTests;

internal static class LiveProductAspire
{
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(10);
    internal const string AppHostPath = "os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj";
    internal const string McpResource = "silo";
    private const string ResourceWaitSeconds = "600";

    internal static async Task RunScenarioAsync(
        string repository,
        IReadOnlyList<string> waitResources,
        Func<Task> scenario,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(waitResources);
        ArgumentNullException.ThrowIfNull(scenario);

        var started = false;
        await RunAsync(
            repository,
            allowFailure: true,
            CancellationToken.None,
            "stop",
            "--apphost",
            AppHostPath,
            "--non-interactive",
            "--nologo");

        try
        {
            await RunAsync(
                repository,
                allowFailure: false,
                cancellationToken,
                "start",
                "--apphost",
                AppHostPath,
                "--format",
                "Json",
                "--non-interactive",
                "--nologo");
            started = true;

            foreach (var resource in waitResources)
            {
                await RunAsync(
                    repository,
                    allowFailure: false,
                    cancellationToken,
                    "wait",
                    resource,
                    "--apphost",
                    AppHostPath,
                    "--timeout",
                    ResourceWaitSeconds,
                    "--non-interactive",
                    "--nologo");
            }

            await scenario();
        }
        finally
        {
            if (started)
            {
                await RunAsync(
                    repository,
                    allowFailure: true,
                    CancellationToken.None,
                    "stop",
                    "--apphost",
                    AppHostPath,
                    "--non-interactive",
                    "--nologo");
            }
        }
    }

    internal static async Task<JsonNode> CallToolAsync(
        string repository,
        string tool,
        string input,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            repository,
            allowFailure: false,
            cancellationToken,
            "mcp",
            "call",
            McpResource,
            tool,
            "--input",
            input,
            "--apphost",
            AppHostPath,
            "--non-interactive",
            "--nologo");
        return LiveProductJson.Parse(result.StandardOutput);
    }

    internal static async Task<JsonObject> WaitForGenAiSpanAsync(
        string repository,
        Func<JsonObject, bool> matches,
        string description,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matches);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var result = await RunAsync(
                repository,
                allowFailure: false,
                cancellationToken,
                "otel",
                "spans",
                "--apphost",
                AppHostPath,
                "--format",
                "Json",
                "--limit",
                "100",
                "--search",
                "gen_ai",
                "--non-interactive",
                "--nologo");
            var spans = LiveProductJson.Parse(result.StandardOutput).AsArray();
            var span = spans.OfType<JsonObject>().FirstOrDefault(matches);

            if (span is not null)
            {
                return span;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new Xunit.Sdk.XunitException(
            $"No matching GenAI span arrived within one minute: {description}.");
    }

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
