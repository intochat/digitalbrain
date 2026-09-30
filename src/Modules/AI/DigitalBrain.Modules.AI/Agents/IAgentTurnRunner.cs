using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI.Agents;

public interface IAgentTurnRunner
{
    IAsyncEnumerable<AgentTurnEvent> RunAsync(AgentTurnRequest request, CancellationToken ct);
}
