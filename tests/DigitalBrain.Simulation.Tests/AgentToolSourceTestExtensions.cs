using DigitalBrain.Abstractions.Identity;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Product.Identity;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Simulation.Tests;

internal static class AgentToolSourceTestExtensions
{
    // These local sources prepare synchronously; MCP sources are tested asynchronously.
    internal static IReadOnlyList<AIFunction> PrepareTestTools(this IAgentToolSource source, OwnerId owner)
    {
        var context = new AgentToolContext(new NeuronId("assistant", owner, "assistant"),
            VerifiedActor.Current?.PrincipalId, new NoRequests());
        var preparation = source.GetToolsAsync(context, CancellationToken.None);
        if (!preparation.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Use an asynchronous preparation test for this source.");
        }
        return preparation.Result.OfType<AIFunction>().ToArray();
    }

    private sealed class NoRequests : IAgentRequests
    {
        public Task<AgentReply> RequestAsync<TAgent>(string instanceName, AgentRequest request,
            CancellationToken cancellationToken = default) where TAgent : IAgent
            => throw new InvalidOperationException("This test has no active neuron request capability.");
    }
}
