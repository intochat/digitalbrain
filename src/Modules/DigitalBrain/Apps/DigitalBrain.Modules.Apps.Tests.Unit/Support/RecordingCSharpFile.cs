using System.Collections.Concurrent;
using DigitalBrain.Core;
using DigitalBrain.Microsoft.CSharp;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

// Replaces the container runner: records the source, settings and lifecycle an installed app asks for.
[GrainType("microsoft.csharp.file")]
public sealed class RecordingCSharpFile : Neuron, ICSharpFile
{
    public static ConcurrentDictionary<string, CSharpFileSnapshot> Files { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Deleted { get; } = new(StringComparer.Ordinal);

    private string Key => this.GetPrimaryKeyString();

    private CSharpFileSnapshot Current => Files.GetOrAdd(Key, key => new(key, "", new Dictionary<string, string>(), CSharpFileStatus.Stopped, null, null, false, 0, null));

    public Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default) => Task.FromResult(Current);

    public Task Write(string source, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Source = source });

    public Task Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Settings = new Dictionary<string, string>(settings) });

    public Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Status = CSharpFileStatus.Running, StartedAt = DateTimeOffset.UtcNow });

    public Task<CSharpFileSnapshot> Arm(CSharpTrigger trigger, CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Trigger = trigger });

    public Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
        => Task.FromResult(Files[Key] = Current with { Status = CSharpFileStatus.Stopped });

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default) => Task.FromResult("");

    public Task Delete(CancellationToken cancellationToken = default)
    {
        Deleted[Key] = true;
        Files[Key] = Current with { Source = "", Settings = new Dictionary<string, string>(), Status = CSharpFileStatus.Stopped };
        return Task.CompletedTask;
    }
}
