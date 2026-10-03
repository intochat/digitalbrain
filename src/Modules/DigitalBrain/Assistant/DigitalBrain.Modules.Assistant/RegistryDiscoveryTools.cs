using System.ComponentModel;
using DigitalBrain.AI.Agents;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Registry;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Assistant;

internal sealed class RegistryDiscoveryTools(IGrainFactory grains, ModuleInventory modules) : IAgentToolFactory
{
    public IReadOnlyList<AIFunction> Create(Func<AgentToolContext> context)
    {
        IRegistry Registry()
        {
            if (context().ScopeId != BrainScope.CurrentId())
            { throw new UnauthorizedAccessException("Discovery must use the caller's current brain."); }
            return grains.GetGrain<IRegistry>(IRegistry.Key);
        }
        bool Available() => modules.ContractAssemblies().Contains(typeof(IRegistry).Assembly);
        static AgentToolOffer Error(string code, string message) => new([], new { isError = true, code, message });

        async Task<AgentToolOffer> Discover(
            [Description("What the user wants to find or do.")] string query,
            CancellationToken ct,
            [Description("Provider ID from an earlier discovery. Omit to list providers.")] string? provider = null,
            int offset = 0, int limit = 10)
        {
            if (!Available()) { return Error("discovery_unavailable", "The Registry module is not installed."); }
            return new([], await Registry().Browse(query, provider, offset, limit, ct));
        }

        async Task<AgentToolOffer> Select(
            [Description("Exact capability ID returned by discovery, such as postgres or apps:owner/name.")] string id,
            CancellationToken ct)
        {
            if (!Available()) { return Error("discovery_unavailable", "The Registry module is not installed."); }
            try
            {
                var capability = await Registry().Select(id, ct);
                return capability is null
                    ? Error("capability_unavailable", "This capability is no longer available in the current brain. Browse its provider before selecting.")
                    : new(capability.Tools, capability,
                        capability.ToolSource is { } source && capability.ToolResource is { } resource ? new(source, resource) : null, ReplaceSelection: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { return Error("selection_unavailable", "The selected capability could not be resolved. Retry discovery."); }
        }

        return [
            AIFunctionFactory.Create(Discover, new AIFunctionFactoryOptions {
                Name = "discover_capabilities",
                Description = "List bounded capability descriptions in the current brain. Pass a provider ID to browse its resources, and NextOffset to continue. Discovery does not enable tools; use select_capability.",
                MarshalResult = static (result, _, _) => new ValueTask<object?>(result) }),
            AIFunctionFactory.Create(Select, new AIFunctionFactoryOptions {
                Name = "select_capability",
                Description = "Resolve a capability in the current brain and enable only its tools for this turn. Access is checked again when invoked.",
                MarshalResult = static (result, _, _) => new ValueTask<object?>(result) }),
        ];
    }
}
