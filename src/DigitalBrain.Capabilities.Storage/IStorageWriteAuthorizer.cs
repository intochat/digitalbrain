namespace DigitalBrain.Capabilities.Storage;

/// <summary>Host infrastructure for already-approved primitive writes. This service is never a neuron contract.</summary>
public interface IStorageWriteAuthorizer
{
    /// <summary>Opens the trusted runner's approved execution scope; callers outside it receive no permit.</summary>
    IDisposable BeginApproved(BrainId brain, string requester);

    /// <summary>Sends one exact storage primitive with a single-use permit when executing an approved plan.</summary>
    Task<T> Invoke<T>(string connection, WritePlan primitive, Func<Task<T>> send, CancellationToken ct);

    /// <summary>Consumes the ambient permit only for this storage neuron, primitive and authenticated caller.</summary>
    Task<bool> Consume(NeuronId storageNeuron, WritePlan primitive, CancellationToken ct);
}

/// <summary>The host's authenticated caller identity; null when this call has no trusted caller in the requested brain.</summary>
public interface IStorageWriteIdentity
{
    /// <summary>Returns an opaque, stable representation of the actual caller, never an app-supplied requester string.</summary>
    string? Current(BrainId brain);
}
