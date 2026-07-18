using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Swarm;

/// <summary>
/// Synapse that triggers a new Roslyn Swarm code analysis session.
/// </summary>
[GenerateSerializer]
public sealed record RequestSwarmAnalysis([property: Id(1)] string ProjectPath,
    [property: Id(2)] int WorkerCount
) : Synapse;

/// <summary>
/// Synapse assigning a specific source document to a swarm worker grain.
/// </summary>
[GenerateSerializer]
public sealed record SwarmDocumentAssigned([property: Id(1)] Guid SessionId,
    [property: Id(2)] string DocumentName,
    [property: Id(3)] string SourceCode
) : Synapse;

/// <summary>
/// Synapse representing a proposed code finding from a worker grain.
/// </summary>
[GenerateSerializer]
public sealed record SwarmFindingProposed([property: Id(1)] Guid SessionId,
    [property: Id(2)] string DocumentName,
    [property: Id(3)] string Severity,
    [property: Id(4)] string FindingMessage,
    [property: Id(5)] int LineNumber
) : Synapse;

/// <summary>
/// Synapse allowing agents to communicate and coordinate within the swarm.
/// </summary>
[GenerateSerializer]
public sealed record SwarmAgentMessage([property: Id(1)] Guid SessionId,
    [property: Id(2)] string SenderName,
    [property: Id(3)] string MessageContent
) : Synapse;

/// <summary>
/// Synapse signaling the completion of the swarm session with final analysis metrics.
/// </summary>
[GenerateSerializer]
public sealed record SwarmSessionCompleted([property: Id(1)] Guid SessionId,
    [property: Id(2)] int TotalFilesReviewed,
    [property: Id(3)] int TotalFindingsFound,
    [property: Id(4)] string SummaryReport
) : Synapse;
