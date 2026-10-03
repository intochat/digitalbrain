using System.Collections.Concurrent;
using DigitalBrain.Kernel;
using DigitalBrain.Microsoft.CSharp;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Replaces the container runner: records the source, settings and lifecycle an installed app asks for.
[GrainType("microsoft.csharp.file")]
public sealed class RecordingCSharpFile : Neuron, ICSharpFile, ICSharpAppBinding
{
    public static ConcurrentDictionary<string, string> AppBindings { get; } = new(StringComparer.Ordinal);
    public Task BindApp(string appId) => Task.FromResult(AppBindings[this.GetPrimaryKeyString()] = appId);
    public static ConcurrentDictionary<string, CSharpFileSnapshot> Files { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Deleted { get; } = new(StringComparer.Ordinal);

    // A verification's tests file whose key starts with one of these prefixes "runs" instantly:
    // Start reports the scripted exit code and ReadLogs the scripted output.
    public static ConcurrentDictionary<string, (int ExitCode, string Logs)> ScriptedRuns { get; } = new(StringComparer.Ordinal);

    private string Key => this.GetPrimaryKeyString();

    private (int ExitCode, string Logs)? Scripted =>
        ScriptedRuns.FirstOrDefault(pair => Key.StartsWith(pair.Key, StringComparison.Ordinal)) is { Key.Length: > 0 } match ? match.Value : null;

    private CSharpFileSnapshot Current => Files.GetOrAdd(Key, key => new(key, "", new Dictionary<string, string>(), CSharpFileStatus.Stopped, null, null, false, 0, null));

    public Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task Write(string source, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Source = source });

    // Kept across Delete, so a test can assert what a since-retired file was configured with.
    public static ConcurrentDictionary<string, Dictionary<string, string>> ConfiguredSettings { get; } = new(StringComparer.Ordinal);

    public Task Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ConfiguredSettings[Key] = new Dictionary<string, string>(settings);
        return Task.FromResult(Files[Key] = Current with { Settings = new Dictionary<string, string>(settings) });
    }

    public Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Scripted is { } scripted
            ? Current with { Status = CSharpFileStatus.Exited, ExitCode = scripted.ExitCode, StartedAt = DateTimeOffset.UtcNow }
            : Current with { Status = CSharpFileStatus.Running, StartedAt = DateTimeOffset.UtcNow });

    public Task<CSharpFileSnapshot> Arm(CSharpTrigger trigger, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Trigger = trigger });

    public Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Status = CSharpFileStatus.Stopped });

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => Task.FromResult(Scripted?.Logs ?? "");

    public Task Delete(CancellationToken cancellationToken = default)
    {
        Deleted[Key] = true;
        Files[Key] = Current with { Source = "", Settings = new Dictionary<string, string>(), Status = CSharpFileStatus.Stopped };
        return Task.CompletedTask;
    }
}
