using Ino.Core;

namespace Ino.Domains.Recall.Contracts;

/// <summary>
/// Synapse fired by <c>RecallPlan</c> when the user asks the system what it
/// remembers about something. Routed to <c>RecallNeuron</c> (the canonical
/// handler), which performs the semantic lookup against the user's Qdrant
/// collection via IAW's <see cref="Core.Memory.IMemoryLookup"/>. The user id
/// flows through <c>NeuronContext.UserId</c>; the grain primary key is
/// correlation-shaped (standard <c>IFirePort</c> dispatch).
/// </summary>
[GenerateSerializer]
public sealed record RecallQuestion(
    [property: Id(0)] string Text) : ISynapse;
