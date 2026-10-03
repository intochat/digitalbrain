using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Files;

[Alias("intochat.image-save-operation"), Orleans.Metadata.DefaultGrainType("intochat.image-save-operation")]
internal interface IImageSaveOperation : INeuron
{
    Task<ImageSaveOperationState> Read();
    Task<ImageSaveOperationState> Prepare(SavedFile result);
    Task Uploaded();
    Task<SavedFile> Commit();
}

[GenerateSerializer, Alias("intochat.image-save-operation-state")]
public sealed record ImageSaveOperationState
{
    [Id(0)] public SavedFile? Result { get; init; }
    [Id(1)] public string Stage { get; init; } = "requested";
}

[GrainType("intochat.image-save-operation")]
internal sealed class ImageSaveOperationNeuron([PersistentState("save-operation", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageSaveOperationState> store) : Neuron, IImageSaveOperation
{
    public Task<ImageSaveOperationState> Read() => Task.FromResult(store.State);
    public async Task<ImageSaveOperationState> Prepare(SavedFile result)
    {
        if (store.State.Result is { } previous)
        {
            if (previous != result) { throw new InvalidOperationException("This save operation was already used for different content."); }
            return store.State;
        }
        await Save(new() { Result = result });
        return store.State;
    }
    public async Task Uploaded()
    {
        if (store.State.Result is null) { throw new InvalidOperationException("Prepare the save first."); }
        if (store.State.Stage != "committed") { await Save(store.State with { Stage = "uploaded" }); }
    }
    public async Task<SavedFile> Commit()
    {
        if (store.State.Stage == "requested") { throw new InvalidOperationException("Upload the save first."); }
        if (store.State.Stage != "committed") { await Save(store.State with { Stage = "committed" }); }
        return store.State.Result!;
    }
    private async Task Save(ImageSaveOperationState next)
    {
        var old = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); }
        catch { store.State = old; throw; }
    }
}
