using System.Text;
using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Microsoft.CSharp;

[GrainType("microsoft.csharp.file")]
internal sealed partial class CSharpFileNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CSharpFileState> store,
    ICSharpRunner runner)
    : Neuron<CSharpFileState>(store), ICSharpFile
{
    internal const int MaximumSourceBytes = 128 * 1024;
    internal const int MaximumSettings = 64;
    private string FileId => this.GetPrimaryKeyString();

    public async Task<CSharpFileSnapshot> Read(CancellationToken cancellationToken = default)
        => Describe(await runner.InspectAsync(FileId, cancellationToken));

    public async Task<CSharpFileSnapshot> Write(string source, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes) { throw new ArgumentException($"Source exceeds {MaximumSourceBytes} bytes.", nameof(source)); }
        var container = await runner.InspectAsync(FileId, cancellationToken);
        await Save(new CSharpFileState { Source = source, Settings = new(Snapshot.Settings, StringComparer.Ordinal) },
            new CSharpFileChanged(FileId, container.Status));
        return Describe(container);
    }

    public async Task<CSharpFileSnapshot> Configure(IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Count > MaximumSettings) { throw new ArgumentException($"At most {MaximumSettings} settings are allowed.", nameof(settings)); }
        if (settings.Keys.FirstOrDefault(name => !SettingName().IsMatch(name)) is { } invalid)
        { throw new ArgumentException($"Setting '{invalid}' must contain only letters, digits and underscores.", nameof(settings)); }
        var container = await runner.InspectAsync(FileId, cancellationToken);
        await Save(new CSharpFileState { Source = Snapshot.Source, Settings = new(settings, StringComparer.Ordinal) },
            new CSharpFileChanged(FileId, container.Status));
        return Describe(container);
    }

    public async Task<CSharpFileSnapshot> Start(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Snapshot.Source)) { throw new InvalidOperationException("Write the C# source before starting it."); }
        await runner.StartAsync(FileId, Snapshot.Source, Snapshot.Settings, cancellationToken);
        return await Changed(cancellationToken);
    }

    public async Task<CSharpFileSnapshot> Stop(CancellationToken cancellationToken = default)
    {
        await runner.StopAsync(FileId, cancellationToken);
        return await Changed(cancellationToken);
    }

    public Task<string> ReadLogs(int tail = 200, CancellationToken cancellationToken = default)
        => runner.LogsAsync(FileId, tail, cancellationToken);

    public async Task Delete(CancellationToken cancellationToken = default)
    {
        await runner.StopAsync(FileId, cancellationToken);
        await Save(new CSharpFileState(), new CSharpFileChanged(FileId, CSharpFileStatus.Stopped));
        DeactivateOnIdle();
    }

    private async Task<CSharpFileSnapshot> Changed(CancellationToken cancellationToken)
    {
        var container = await runner.InspectAsync(FileId, cancellationToken);
        await PublishAsync(new CSharpFileChanged(FileId, container.Status));
        return Describe(container);
    }

    private CSharpFileSnapshot Describe(CSharpContainerState container)
        => new(FileId, Snapshot.Source, new Dictionary<string, string>(Snapshot.Settings, StringComparer.Ordinal),
            container.Status, container.ExitCode, container.StartedAt);

    [GeneratedRegex("^[A-Za-z0-9_]{1,128}$")]
    private static partial Regex SettingName();
}
