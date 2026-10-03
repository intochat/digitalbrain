using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Assistant;

// Adapts Registry discovery to the AI tool-offer protocol. Registry and module
// providers own discovery; neither tool names nor scope come from model arguments.
internal sealed class RegistryDiscoveryTools(IGrainFactory grains, ModuleInventory modules) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        async Task<AgentToolOffer> Discover(
            [Description("What the user wants to find or do, for example PostgreSQL tables or an installed app.")] string query,
            CancellationToken ct)
        {
            if (context().ScopeId != BrainScope.CurrentId())
            { throw new UnauthorizedAccessException("Discovery must use the caller's current brain."); }
            if (!modules.ContractAssemblies().Contains(typeof(IRegistry).Assembly))
            {
                return new([], new
                {
                    isError = true,
                    code = "discovery_unavailable",
                    message = "The Registry module is not installed on this host. Capability discovery is unavailable."
                });
            }
            var discovery = await grains.GetGrain<IRegistry>(IRegistry.Key).Discover(query, ct);
            return new(discovery.Capabilities.SelectMany(item => item.Tools).Distinct(StringComparer.Ordinal).ToArray(), discovery);
        }

        return [AIFunctionFactory.Create(Discover, new AIFunctionFactoryOptions
        {
            Name = "discover_capabilities",
            Description = "Discover capabilities and resources authorized in the current brain. Returns descriptions and makes their tools available for subsequent calls. Discovery errors are partial failures, not proof that a resource does not exist.",
            MarshalResult = static (result, _, _) => new ValueTask<object?>(result),
        })];
    }
}
