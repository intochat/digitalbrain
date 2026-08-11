using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.blocker")]
public abstract record ExecutionBlocker
{
    protected ExecutionBlocker(BlockerId id)
    {
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("An execution blocker id cannot be empty.", nameof(id));
        }

        Id = id;
    }

    [Id(0)]
    public BlockerId Id { get; }
}

[GenerateSerializer]
[Alias("db.execution.input-required")]
public sealed record InputRequired(BlockerId Id) : ExecutionBlocker(Id);

[GenerateSerializer]
[Alias("db.execution.approval-required")]
public sealed record ApprovalRequired(BlockerId Id) : ExecutionBlocker(Id);

[GenerateSerializer]
[Alias("db.execution.dependency-pending")]
public sealed record DependencyPending(BlockerId Id) : ExecutionBlocker(Id);

[GenerateSerializer]
[Alias("db.execution.retry-scheduled")]
public sealed record RetryScheduled(BlockerId Id) : ExecutionBlocker(Id);

[GenerateSerializer]
[Alias("db.execution.outcome-uncertain")]
public sealed record OutcomeUncertain(BlockerId Id) : ExecutionBlocker(Id);

[GenerateSerializer]
[Alias("db.execution.user-action-pending")]
public sealed record UserActionPending : ExecutionBlocker
{
    public UserActionPending(
        BlockerId id,
        NeuronId module,
        string moduleId,
        string displayText,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long parkRevision,
        DateTimeOffset expiresAt,
        NeuronId completer)
        : base(id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);

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

        Module = module;
        ModuleId = moduleId.Trim();
        DisplayText = displayText.Trim();
        ActionReference = actionReference;
        ActionEpoch = actionEpoch;
        ParkRevision = parkRevision;
        ExpiresAt = expiresAt;
        Completer = completer;
    }

    [Id(1)]
    public NeuronId Module { get; init; }

    [Id(2)]
    public string ModuleId { get; init; }

    [Id(3)]
    public string DisplayText { get; init; }

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
