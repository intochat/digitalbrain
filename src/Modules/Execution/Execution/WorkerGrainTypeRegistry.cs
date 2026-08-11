using System.Collections.Concurrent;

namespace DigitalBrain.Execution;

public interface IWorkerTypeRegistration
{
    string GrainType { get; }
}

public sealed record WorkerTypeRegistration(string GrainType) : IWorkerTypeRegistration;

// Allow-list of grain types that may receive worker-dispatch envelopes.
// Modules explicitly register their domain adapters via IWorkerTypeRegistration.
public sealed class WorkerGrainTypeRegistry
{
    private readonly ConcurrentDictionary<string, byte> _allowed =
        new(StringComparer.OrdinalIgnoreCase);

    public WorkerGrainTypeRegistry Allow(string grainType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainType);
        _allowed[grainType.Trim()] = 0;
        return this;
    }

    public bool IsAllowed(string grainType)
        => !string.IsNullOrWhiteSpace(grainType) && _allowed.ContainsKey(grainType);
}
