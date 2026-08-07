using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.user-action-required")]
[Description("Module-owned user action required before a task attempt can continue")]
public sealed record UserActionRequired : Synapse
{
    public UserActionRequired(
        NeuronId task,
        AttemptId attempt,
        NeuronId module,
        string moduleId,
        string displayText,
        ProtectedPayloadReference actionReference,
        Guid actionEpoch,
        long parkRevision,
        DateTimeOffset expiresAt,
        NeuronId completer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayText);

        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("Task identity is required.", nameof(task));
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

        if (completer.Owner != task.Owner)
        {
            throw new ArgumentException("Completer must share the task owner.", nameof(completer));
        }

        Task = task;
        Attempt = attempt;
        Module = module;
        ModuleId = moduleId.Trim();
        DisplayText = displayText.Trim();
        ActionReference = actionReference;
        ActionEpoch = actionEpoch;
        ParkRevision = parkRevision;
        ExpiresAt = expiresAt;
        Completer = completer;
    }

    [Id(0)]
    public NeuronId Task { get; init; }

    [Id(1)]
    public AttemptId Attempt { get; init; }

    [Id(2)]
    public NeuronId Module { get; init; }

    [Id(3)]
    public string ModuleId { get; init; }

    [Id(4)]
    public string DisplayText { get; init; }

    [Id(5)]
    public ProtectedPayloadReference ActionReference { get; init; }

    [Id(6)]
    public Guid ActionEpoch { get; init; }

    [Id(7)]
    public long ParkRevision { get; init; }

    [Id(8)]
    public DateTimeOffset ExpiresAt { get; init; }

    [Id(9)]
    public NeuronId Completer { get; init; }
}

[GenerateSerializer]
[Alias("tasks.complete-user-action")]
[Description("Bridge-owned completion of a parked module user action")]
public sealed record CompleteUserAction(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

[GenerateSerializer]
[Alias("tasks.deny-user-action")]
[Description("Bridge-owned denial of a parked module user action")]
public sealed record DenyUserAction(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ProtectedPayloadReference ActionReference,
    [property: Id(2)] Guid ActionEpoch,
    [property: Id(3)] long ExpectedParkRevision) : Synapse;

[GenerateSerializer]
[Alias("tasks.user-action-denied")]
[Description("Stable failure when a required module user action is denied")]
public sealed record UserActionDenied : Failure
{
    public UserActionDenied(string moduleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ModuleId = moduleId.Trim();
    }

    [Id(0)]
    public string ModuleId { get; init; }
}

[GenerateSerializer]
[Alias("tasks.user-action-park-ready")]
[Description("Task notifies the bound completer that the matching user-action park is durable")]
public sealed record UserActionParkReady : Synapse
{
    public UserActionParkReady(
        NeuronId task,
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

        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("Task identity is required.", nameof(task));
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

        if (completer.Owner != task.Owner)
        {
            throw new ArgumentException("Completer must share the task owner.", nameof(completer));
        }

        Task = task;
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
    public NeuronId Task { get; init; }

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
