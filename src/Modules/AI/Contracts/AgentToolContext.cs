using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.AI;

// A local, turn-scoped capability. Never put this in serializable RequestContext.
public sealed record AgentToolContext(
    OwnerId Owner,
    PrincipalId? Principal,
    IAgentRequests Requests);

public interface IAgentRequests
{
    Task<AgentReply> RequestAsync<TAgent>(
        string instanceName,
        AgentRequest request,
        CancellationToken cancellationToken = default)
        where TAgent : IAgent;
}
