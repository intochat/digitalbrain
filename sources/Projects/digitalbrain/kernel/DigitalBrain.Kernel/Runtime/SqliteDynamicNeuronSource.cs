using DigitalBrain.InoLang;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

public sealed class SqliteDynamicNeuronSource(
    DynamicDomainRegistry dbRegistry,
    IContractCatalog catalog,
    ILogger<SqliteDynamicNeuronSource> logger) : IInterpretedNeuronSource
{
    public async Task<IReadOnlyList<InterpretedNeuronRegistration>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var registrations = new List<InterpretedNeuronRegistration>();

        var records = await dbRegistry.GetAllNeuronsAsync(cancellationToken).ConfigureAwait(false);
        foreach (var r in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compiled = InoCompiler.Compile(r.SourceCode, catalog);
            if (!compiled.Success || compiled.Linked is null)
            {
                logger.LogWarning(
                    "SqliteDynamicNeuronSource: Dynamic neuron '{Fqn}' failed to compile/link at start; skipping. Diagnostics: {Diagnostics}",
                    r.Fqn,
                    string.Join(" | ", compiled.Diagnostics.Select(d => d.Code + " " + d.Message)));
                continue;
            }

            registrations.Add(LinkedPortCatalogContributor.BuildRegistration(r.SourceCode, compiled.Linked));
        }

        logger.LogInformation("SqliteDynamicNeuronSource loaded {Count} dynamic neurons from SQLite.", registrations.Count);
        return registrations;
    }
}
