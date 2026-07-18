using System.Reflection;
using DigitalBrain.InoLang.Linking;

namespace DigitalBrain.Kernel.Runtime;

// E-RUN #44. Hosted-service adapter for NeuronCatalogInvariantVerifier. Runs
// at silo StartAsync, before the gateway accepts requests, and throws
// InvalidOperationException with every violation aggregated into one
// message — so an operator fixes all defects in one re-boot cycle rather
// than peeling them off one at a time.
//
// Throwing matches CLAUDE.md "Internal invariants are still throws": a
// drifted neuron/catalog pair is a substrate defect, not a runtime input, and
// emitting a failure synapse from a hosted service has no addressee.
//
// Discovery defaults to AppDomain.CurrentDomain.GetAssemblies() so the
// production silo verifies every loaded neuron grain. The secondary
// constructor lets tests inject a curated assembly set without exercising
// the AppDomain.
public sealed class NeuronCatalogInvariantHostedService(
    IContractCatalog catalog,
    IEnumerable<Assembly> assemblies,
    ILogger<NeuronCatalogInvariantHostedService> logger) : IHostedService
{
    public NeuronCatalogInvariantHostedService(
        IContractCatalog catalog,
        ILogger<NeuronCatalogInvariantHostedService> logger)
        : this(catalog, AppDomain.CurrentDomain.GetAssemblies(), logger)
    {
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var result = NeuronCatalogInvariantVerifier.Verify(catalog, assemblies);
        if (result.IsValid)
        {
            logger.LogInformation(
                "Neuron/catalog invariant verified: every loaded neuron grain's [GrainType] resolves to a ContractKind.Neuron entry in the catalog.");
            return Task.CompletedTask;
        }

        var summary = string.Join(
            Environment.NewLine,
            result.Violations.Select(v => $"  - {v.Reason}"));
        throw new InvalidOperationException(
            $"Neuron/catalog invariant violated ({result.Violations.Count} defect(s)). " +
            "The silo will not accept gateway traffic until the .ino → grain dispatch surface is consistent." +
            Environment.NewLine + summary);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
