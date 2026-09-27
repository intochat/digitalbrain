using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.CSharp;

[GrainType("microsoft.csharp.file")]
internal sealed partial class CSharpFileNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CSharpFileState> store,
    SandboxCSharpRunner runner)
    : Neuron<CSharpFileState>(store), ICSharpFile
{
    internal const int MaximumSourceBytes = 128 * 1024;
    private const int MaximumSettings = 64;
    private string FileId => this.GetPrimaryKeyString();

    public async Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default)
        => Describe(await runner.InspectAsync(Snapshot.RunId, cancellationToken));

    public Task Write(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes) { throw new ArgumentException($"Source exceeds {MaximumSourceBytes} bytes.", nameof(source)); }
        return Save(Snapshot with { Source = source }, new CSharpFileChanged(FileId));
    }

    public Task Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > MaximumSettings) { throw new ArgumentException($"At most {MaximumSettings} settings are allowed.", nameof(settings)); }
        if (settings.Keys.FirstOrDefault(name => !SettingName().IsMatch(name)) is { } invalid)
        { throw new ArgumentException($"Setting '{invalid}' must contain only letters, digits and underscores.", nameof(settings)); }
        return Save(Snapshot with { Settings = new(settings, StringComparer.Ordinal) }, new CSharpFileChanged(FileId));
    }

    public async Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Snapshot.Source)) { throw new InvalidOperationException("Write the C# source before starting it."); }
        if (Snapshot.RunId.Length > 0) { await runner.StopAsync(Snapshot.RunId, cancellationToken); }
        var runId = Guid.NewGuid().ToString("N");
        await runner.StartAsync(runId, Snapshot.Source, Snapshot.Settings, cancellationToken);
        await Save(Snapshot with { RunId = runId }, new CSharpFileChanged(FileId));
        return Describe(await runner.InspectAsync(runId, cancellationToken));
    }

    public async Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
    {
        await runner.StopAsync(Snapshot.RunId, cancellationToken);
        var run = await runner.InspectAsync(Snapshot.RunId, cancellationToken);
        await PublishAsync(new CSharpFileChanged(FileId));
        return Describe(run);
    }

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => runner.LogsAsync(Snapshot.RunId, tail, cancellationToken);

    public async Task Delete(CancellationToken cancellationToken = default)
    {
        if (Snapshot.RunId.Length > 0) { await runner.StopAsync(Snapshot.RunId, cancellationToken); }
        await Save(new CSharpFileState(), new CSharpFileChanged(FileId));
        DeactivateOnIdle();
    }

    private CSharpFileSnapshot Describe(CSharpRunState run)
        => new(FileId, Snapshot.Source, Snapshot.Settings, run.Status, run.ExitCode, run.StartedAt);

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex SettingName();
}
