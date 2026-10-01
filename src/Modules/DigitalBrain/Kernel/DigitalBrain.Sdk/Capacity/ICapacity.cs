namespace DigitalBrain.Sdk.Capacity;

public sealed record CapacityScope(string BrainId, string? AppId);

public sealed record ResolvedCapacity(string Kind, string Origin);

// Resolution order is the platform's law: the brain's account (future) -> configured source -> provisioner.
public interface ICapacity
{
    ValueTask<ResolvedCapacity> Resolve(string kind, CapacityScope scope, CancellationToken ct = default);

    ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default);
}

// A deployment-configured source for one kind (production: the platform connection).
public interface ICapacityConfiguredSource
{
    string Kind { get; }
    string Origin { get; }
}

// Creates (idempotently) capacity for a scope; registered only where the environment supports it.
public interface ICapacityProvisioner
{
    string Kind { get; }
    ValueTask<string> Ensure(CapacityScope scope, CancellationToken ct);
}
