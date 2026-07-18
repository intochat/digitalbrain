using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Runtime;

namespace DigitalBrain.Kernel.Runtime;

// E-SDK #63. The registry is BOTH (a) a singleton lookup the grain reads
// during OnActivateAsync to auto-configure itself from a known descriptor,
// and (b) an IHostedService that, at silo start, asks each registered
// IInterpretedNeuronSource for contributions and publishes their catalog
// entries to IBrainCatalog. The grain auto-configure stays *lazy* (per
// activation), but the catalog entries must be published *eagerly* so the
// Navigator's IsInterpreted routing branch and the SynapseBroadcaster's
// HandledSignalSubscriptions fan-out can see them.
public sealed class InterpretedNeuronRegistry(
    IEnumerable<IInterpretedNeuronSource> sources,
    IServiceProvider serviceProvider,
    ILogger<InterpretedNeuronRegistry> logger) : IHostedService, IInterpretedNeuronRegistry
{
    private IGrainFactory GrainFactory => serviceProvider.GetRequiredService<IGrainFactory>();

    readonly ConcurrentDictionary<string, InterpretedNeuronRegistration> _byFqn =
        new(StringComparer.Ordinal);

    public bool TryGet(string fqn, [NotNullWhen(true)] out InterpretedNeuronRegistration? registration)
    {
        return _byFqn.TryGetValue(fqn, out registration);
    }

    public System.Collections.Generic.IReadOnlyCollection<string> RegisteredFqns => [.. _byFqn.Keys];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var contributions = new List<InterpretedNeuronRegistration>();
        foreach (var source in sources)
        {
            var discovered = await source.DiscoverAsync(cancellationToken);
            contributions.AddRange(discovered);
        }

        var addedContributions = new List<InterpretedNeuronRegistration>();
        foreach (var registration in contributions)
        {
            // Fail-fast on duplicates rather than last-source-wins: once
            // marketplace bundles and Creator-persistence can both
            // contribute, a silent overwrite would let a maliciously-named
            // bundle shadow a Creator-authored neuron at the same FQN.
            if (_byFqn.TryGetValue(registration.Descriptor.Fqn, out var existing))
            {
                if (existing.Descriptor.InoLangSource == registration.Descriptor.InoLangSource
                    && existing.Descriptor.InoLangSourceCacheKey == registration.Descriptor.InoLangSourceCacheKey
                    && existing.Descriptor.InoLangSourceSha256 == registration.Descriptor.InoLangSourceSha256)
                {
                    logger.LogInformation(
                        "InterpretedNeuronRegistry: Ignored exact duplicate registration for FQN '{Fqn}'.",
                        registration.Descriptor.Fqn);
                    continue;
                }

                logger.LogWarning(
                    "InterpretedNeuronRegistry: FQN '{Fqn}' is already registered with different source. Skipping duplicate.",
                    registration.Descriptor.Fqn);
                continue;
            }

            if (_byFqn.TryAdd(registration.Descriptor.Fqn, registration))
            {
                addedContributions.Add(registration);
            }
        }

        // Publish catalog entries so the gateway's IsInterpreted branch
        // resolves and SynapseBroadcaster's subscription fan-out targets the
        // grain. The grain is NOT eagerly configured here — that's lazy on
        // first activation per the grain's OnActivateAsync override.
        if (addedContributions.Count > 0)
        {
            var catalog = GrainFactory.GetGrain<IBrainCatalog>(BrainScopeHelper.GlobalScope);
            foreach (var registration in addedContributions)
            {
                var entry = LinkedPortCatalogContributor.BuildEntry(
                    registration.Descriptor,
                    registration.HandledSignalSubscriptions);
                await catalog.RegisterAsync(entry);
            }

            logger.LogInformation(
                "InterpretedNeuronRegistry registered {Count} interpreted neuron(s) to BrainCatalog.",
                addedContributions.Count);
        }
    }

    public async Task RegisterDynamicAsync(InterpretedNeuronRegistration registration)
    {
        _byFqn[registration.Descriptor.Fqn] = registration;

        var catalog = GrainFactory.GetGrain<IBrainCatalog>(BrainScopeHelper.GetActiveScope());
        var entry = LinkedPortCatalogContributor.BuildEntry(
            registration.Descriptor,
            registration.HandledSignalSubscriptions);
        await catalog.RegisterAsync(entry);

        logger.LogInformation(
            "InterpretedNeuronRegistry dynamically registered interpreted neuron '{Fqn}' to BrainCatalog.",
            registration.Descriptor.Fqn);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
