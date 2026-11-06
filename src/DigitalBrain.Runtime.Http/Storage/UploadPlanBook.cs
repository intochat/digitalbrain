using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;

namespace DigitalBrain.Runtime.Http.Storage;

/// <summary>A parked overwrite. The file is unchanged until a person approves and a ticket bound to <see cref="PlanId"/> is used.</summary>
public sealed class ParkedOverwrite
{
    internal ParkedOverwrite(string planId, string brain, string storage, string folder, string name, WritePlan plan)
    {
        PlanId = planId;
        Brain = brain;
        Storage = storage;
        Folder = folder;
        Name = name;
        Plan = plan;
    }

    public string PlanId { get; }
    public string Brain { get; }
    public string Storage { get; }
    public string Folder { get; }
    public string Name { get; }
    public WritePlan Plan { get; }
    public bool Started { get; internal set; }
    public ConfirmationOutcome? Outcome { get; internal set; }
}

/// <summary>
/// Overwrite plans created when a PUT hits an existing name. Not started here: the caller starts one through
/// <c>IOperations.Start</c> and a person approves it. The ticket endpoint reads the outcome.
/// </summary>
public sealed class UploadPlanBook
{
    private readonly Dictionary<string, ParkedOverwrite> _plans = new(StringComparer.Ordinal);

    public ParkedOverwrite Park(string brain, string storage, ItemId folder, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brain);
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var parked = new ParkedOverwrite(
            "op-" + Guid.NewGuid().ToString("N"),
            brain,
            storage,
            folder.Value,
            name,
            WritePlan.WriteFile(folder, name, conflict: ConflictPolicy.Overwrite));
        lock (_plans) { _plans[parked.PlanId] = parked; }
        return parked;
    }

    public ParkedOverwrite? Find(string? planId)
    {
        if (string.IsNullOrEmpty(planId)) { return null; }
        lock (_plans) { return _plans.GetValueOrDefault(planId); }
    }

    public void MarkStarted(string planId)
    {
        var parked = Find(planId) ?? throw new KeyNotFoundException("No parked overwrite has that id.");
        parked.Started = true;
    }

    /// <summary>A person approves. An app-stamped caller cannot.</summary>
    public bool Approve(string planId, CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!string.IsNullOrEmpty(caller.AppId)) { return false; }
        var parked = Find(planId);
        if (parked is null) { return false; }
        parked.Outcome = ConfirmationOutcome.Approved;
        return true;
    }

    public void Deny(string planId) => Set(planId, ConfirmationOutcome.Denied);

    public void Expire(string planId) => Set(planId, ConfirmationOutcome.Expired);

    private void Set(string planId, ConfirmationOutcome outcome)
    {
        var parked = Find(planId) ?? throw new KeyNotFoundException("No parked overwrite has that id.");
        parked.Outcome = outcome;
    }
}
