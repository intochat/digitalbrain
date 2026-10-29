using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing;

// The runtime's operation semantics in TestOS (05-11): the shared StorageWriteRunner over the storage connections bound to the
// TestOS, owner by Stat (first connection that knows the id), and results from the in-memory connections' own operations.
internal static class TestOSOperations
{
    public static StorageWriteRunner Runner(IServiceProvider services, BrainId brain)
        => new((item, ct) => Owner(services, brain, item, ct), Result);

    private static async Task<CapabilityConnection<IStorage>> Owner(IServiceProvider services, BrainId brain, ItemId item, CancellationToken ct)
    {
        if (services.GetService(typeof(ICapabilityBroker)) is not ICapabilityBroker broker) { throw new Unavailable(item); }
        var connections = await broker.ResolveAll<IStorage>(brain, ct).ConfigureAwait(false);
        if (connections.Count == 0) { throw new Unavailable(item); }
        if (connections.Count == 1) { return connections[0]; }
        foreach (var connection in connections)
        {
            try
            {
                _ = await connection.Capability.Stat(item, ct).ConfigureAwait(false);
                return connection;
            }
            catch (StorageException error) when (error is NotFound or Denied or Unsupported)
            {
                // Not this connection's id.
            }
        }
        throw new NotFound(item);
    }

    private static Task<OperationResult> Result(IStorage storage, OperationId id, CancellationToken ct)
        => storage is InMemoryStorage memory
            ? memory.Operations.WaitFor(id, ct)
            : throw new Unsupported(null, new NotSupportedException($"TestOS reads write results from in-memory storage fakes only, not {storage.GetType().Name}."));
}
