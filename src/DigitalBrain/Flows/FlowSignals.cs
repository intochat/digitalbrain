namespace DigitalBrain.Flows;

/// <summary>How a step ran.</summary>
public enum StepOutcome
{
    /// <summary>The body started.</summary>
    Started,

    /// <summary>The step completed.</summary>
    Completed,

    /// <summary>The body failed.</summary>
    Failed,
}

/// <summary>How a wait ended.</summary>
public enum WaitOutcome
{
    /// <summary>A person approved.</summary>
    Approved,

    /// <summary>A person or a rule denied.</summary>
    Denied,

    /// <summary>The awaited signal or time arrived.</summary>
    Arrived,

    /// <summary>The wait expired.</summary>
    Expired,

    /// <summary>The wait was cancelled.</summary>
    Cancelled,
}

/// <summary>How a flow ended.</summary>
public enum FlowOutcome
{
    /// <summary>Every step completed.</summary>
    Completed,

    /// <summary>A step failed.</summary>
    Failed,

    /// <summary>Cancelled, denied or expired.</summary>
    Cancelled,
}

/// <summary>A flow started.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Type">The flow's neuron type.</param>
/// <param name="Title">The display title.</param>
[Signal("flow.started")]
public sealed record FlowStarted(string FlowId, string Type, string Title) : Signal;

/// <summary>A step started, completed or failed.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Step">The step.</param>
/// <param name="Outcome">What happened.</param>
/// <param name="Detail">The failure detail, if any.</param>
[Signal("flow.step")]
public sealed record FlowStep(string FlowId, string Step, StepOutcome Outcome, string? Detail) : Signal;

/// <summary>A step waits.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Step">The step.</param>
/// <param name="Reason">What it waits for.</param>
/// <param name="ConfirmationPlanId">The confirmation plan of an approval wait.</param>
[Signal("flow.waiting")]
public sealed record FlowWaiting(string FlowId, string Step, WaitReason Reason, string? ConfirmationPlanId) : Signal;

/// <summary>A wait ended.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Step">The step that waited.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="By">Who answered, if known.</param>
[Signal("flow.resolve")]
public sealed record FlowResolve(string FlowId, string Step, WaitOutcome Outcome, string? By) : Signal;

/// <summary>A flow ended.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Detail">Why it failed or was cancelled, if known.</param>
[Signal("flow.done")]
public sealed record FlowDone(string FlowId, FlowOutcome Outcome, string? Detail) : Signal;

/// <summary>A relation was added to a flow.</summary>
/// <param name="FlowId">The flow id.</param>
/// <param name="Relation">The relation.</param>
[Signal("flow.related")]
public sealed record FlowRelated(string FlowId, FlowRelation Relation) : Signal;
