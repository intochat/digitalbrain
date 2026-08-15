using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.user-action-park-ready")]
public sealed record UserActionParkReady : Synapse
{
    public UserActionParkReady(
        NeuronId execution,
        AttemptId attempt,
        NeuronId module,
        string moduleId,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long parkRevision,
        DateTimeOffset expiresAt,
        NeuronId completer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        if (execution == default || string.IsNullOrWhiteSpace(execution.Type) || string.IsNullOrWhiteSpace(execution.Name))
        {
            throw new ArgumentException("Execution identity is required.", nameof(execution));
        }

        if (attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("Attempt identity is required.", nameof(attempt));
        }

        if (module == default || string.IsNullOrWhiteSpace(module.Type) || string.IsNullOrWhiteSpace(module.Name))
        {
            throw new ArgumentException("Module neuron identity is required.", nameof(module));
        }

        if (actionReference.Id == Guid.Empty)
        {
            throw new ArgumentException("Action reference is required.", nameof(actionReference));
        }

        if (actionEpoch == Guid.Empty)
        {
            throw new ArgumentException("Action epoch is required.", nameof(actionEpoch));
        }

        if (parkRevision < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(parkRevision), parkRevision, "Park revision cannot be negative.");
        }

        if (completer == default || string.IsNullOrWhiteSpace(completer.Type) || string.IsNullOrWhiteSpace(completer.Name))
        {
            throw new ArgumentException("Completer neuron identity is required.", nameof(completer));
        }

        if (completer.Owner != execution.Owner)
        {
            throw new ArgumentException("Completer must share the execution owner.", nameof(completer));
        }

        Execution = execution;
        Attempt = attempt;
        Module = module;
        ModuleId = moduleId.Trim();
        ActionReference = actionReference;
        ActionEpoch = actionEpoch;
        ParkRevision = parkRevision;
        ExpiresAt = expiresAt;
        Completer = completer;
    }

    [Id(0)]
    public NeuronId Execution { get; init; }

    [Id(1)]
    public AttemptId Attempt { get; init; }

    [Id(2)]
    public NeuronId Module { get; init; }

    [Id(3)]
    public string ModuleId { get; init; }

    [Id(4)]
    public ProtectedPayloadReference ActionReference { get; init; }

    [Id(5)]
    public Guid ActionEpoch { get; init; }

    [Id(6)]
    public long ParkRevision { get; init; }

    [Id(7)]
    public DateTimeOffset ExpiresAt { get; init; }

    [Id(8)]
    public NeuronId Completer { get; init; }
}

