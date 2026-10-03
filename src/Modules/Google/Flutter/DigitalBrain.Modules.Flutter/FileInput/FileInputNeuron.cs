using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.FileInput;

[GrainType(UIVocabulary.FileInputType)]
internal sealed class FileInputNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<FileInputState> store)
    : Neuron<FileInputState>(store), IFileInput
{
    public Task Configure(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Revision++;
        next.Label = label.Trim();
        return Save(next, new FileInputChanged(next.Name, next.Revision));
    }
    public async Task Capture(string name, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(content);
        if (name.Length > 255 || name.Any(char.IsControl)) { throw new ArgumentException("A filename has at most 255 printable characters.", nameof(name)); }
        if (content.Length > 32_000) { throw new ArgumentException("File text has at most 32000 characters.", nameof(content)); }
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Revision++;
        var captured = new FileCaptured(next.Name, name, content);
        await Save(next, new FileInputChanged(next.Name, next.Revision), captured);
        await GrainFactory.GetGrain<IUiBinding>(next.Name).Dispatch(captured);
    }
    public Task<FileInputState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
