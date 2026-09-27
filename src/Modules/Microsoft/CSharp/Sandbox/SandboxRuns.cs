using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Microsoft.CSharp.Sandbox;

// Several scripts share this container, one working directory and one process per run identifier.
internal sealed partial class SandboxRuns(IScriptLauncher launcher, IOptions<SandboxOptions> options, TimeProvider time)
{
    internal static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, SandboxRun> _runs = new(StringComparer.Ordinal);

    public async Task<RunStatus> StartAsync(string identifier, RunRequest request, CancellationToken cancellationToken)
    {
        if (!Identifier().IsMatch(identifier)) { throw new ArgumentException("A run identifier is 4 to 128 letters, digits or dashes.", nameof(identifier)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        if (_runs.TryGetValue(identifier, out var existing) && !existing.Process.Completion.IsCompleted)
        { throw new InvalidOperationException($"Run '{identifier}' is already running."); }

        var workDirectory = Path.Combine(options.Value.WorkRoot, identifier);
        Directory.CreateDirectory(workDirectory);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "app.cs"), request.Source, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(workDirectory, "Directory.Build.props"), BuildProps(options.Value.ClientProject), cancellationToken).ConfigureAwait(false);

        var log = new RunLog(options.Value.LogLines);
        var process = launcher.Start(workDirectory, request.Environment ?? [], log.Append);
        var run = new SandboxRun(identifier, process, log, time.GetUtcNow());
        _runs[identifier] = run;
        return run.Describe();
    }

    public RunStatus? Find(string identifier) => _runs.TryGetValue(identifier, out var run) ? run.Describe() : null;

    public string? ReadLogs(string identifier, int tail) => _runs.TryGetValue(identifier, out var run) ? run.Log.Tail(tail) : null;

    public async Task<RunStatus?> StopAsync(string identifier)
    {
        if (!_runs.TryGetValue(identifier, out var run)) { return null; }
        await run.Process.StopAsync(StopGrace).ConfigureAwait(false);
        return run.Describe();
    }

    // The script's folder picks this up: it references the brain client and imports its namespaces.
    private static string BuildProps(string clientProject) => $"""
        <Project>
          <PropertyGroup>
            <PublishAot>false</PublishAot>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
          </PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="{clientProject}" />
            <Using Include="DigitalBrain.Contracts" />
            <Using Include="DigitalBrain.Client" />
          </ItemGroup>
        </Project>
        """;

    [GeneratedRegex("^[A-Za-z0-9-]{4,128}$")]
    private static partial Regex Identifier();

    private sealed record SandboxRun(string Identifier, IScriptProcess Process, RunLog Log, DateTimeOffset StartedAt)
    {
        public RunStatus Describe() => Process.Completion.IsCompletedSuccessfully
            ? new(Identifier, RunStatuses.Exited, Process.Completion.Result, StartedAt)
            : new(Identifier, Process.Completion.IsCompleted ? RunStatuses.Exited : RunStatuses.Running, null, StartedAt);
    }
}
