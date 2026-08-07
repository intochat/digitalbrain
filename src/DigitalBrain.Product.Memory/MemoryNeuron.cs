using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Product.Memory;

public sealed class MemoryNeuron(IMemoryStore store) : Neuron,
    INeuron<MemoryStoreRequested>,
    INeuron<MemorySearchRequested>,
    INeuron<MemoryRemoveRequested>
{
    public const string Kind = "memory";

    private readonly IMemoryStore store = store ?? throw new ArgumentNullException(nameof(store));

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The provider boundary deliberately converts all non-cancellation failures into optional-memory availability facts.")]
    public async Task HandleAsync(MemoryStoreRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _ = await store.StoreAsync(synapse.Entry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(new MemoryUnavailable("store"));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The provider boundary deliberately converts all non-cancellation failures into optional-memory availability facts.")]
    public async Task HandleAsync(MemorySearchRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var hits = await store.SearchAsync(synapse.Query, cancellationToken);
            Emit(new MemorySearchCompleted(synapse.Query, hits));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(new MemoryUnavailable("search"));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The provider boundary deliberately converts all non-cancellation failures into optional-memory availability facts.")]
    public async Task HandleAsync(MemoryRemoveRequested synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await store.RemoveAsync(synapse.EntryId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Emit(new MemoryUnavailable("remove"));
        }
    }
}
