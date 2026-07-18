using DigitalBrain.Protocol.Domain.Events;
namespace DigitalBrain.Os.Domain.Events;

// Targeted use of latest native C# union (C# 15 / .NET 11 preview `union` keyword, per official docs fetched before edit).
// Replaces loose "string Status" with closed, exhaustive DDD variant for safety + expressiveness (each case carries exact data).
// This is under the "neurons + synapses" model for experiences (kernel-tasks experience reacts to these synapses).
// Cases are serializable records for Orleans. The union itself will be used in the data model (KernelTask).
// Exhaustive matching will be used in supervisor / handlers for safety (no forgotten status).
// Part of Bundle/Experiences/Neurons/Synapses standard: experiences bundle neurons that handle specific union cases (synapses).

[GenerateSerializer]
public sealed record TaskRunning;

[GenerateSerializer]
public sealed record TaskSuspended;

[GenerateSerializer]
public sealed record TaskCompleted(string? Result = null);

[GenerateSerializer]
public sealed record TaskFailed(string Reason);

[GenerateSerializer]
public union KernelTaskStatus(TaskRunning, TaskSuspended, TaskCompleted, TaskFailed);

[GenerateSerializer]
public sealed record KernelTask(
    string Id,
    string Description,
    KernelTaskStatus Status,
    List<string> Logs);

[GenerateSerializer]
public sealed record KernelTaskStarted(string TaskId, string Description) : Synapse;

[GenerateSerializer]
public sealed record KernelTaskStatusChanged(string TaskId, KernelTaskStatus Status) : Synapse;  // now carries the native DU directly (exhaustive + expressive DDD)

[GenerateSerializer]
public sealed record KernelTaskLogAppended(string TaskId, string Line) : Synapse;

[GenerateSerializer]
public sealed record InspectKernelTask(string TaskId) : Synapse;  // sent from UI widget tap (OnTap synapse) to trigger detail

[GenerateSerializer]
public sealed record KernelTaskDetailSurface(string TaskId, KernelTask Task) : Synapse;