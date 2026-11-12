using DigitalBrain.Contracts;
using DigitalBrain.Units;

namespace DigitalBrain.Runtime.Synapses;

/// <summary>
/// Stateless compiler and deployment façade implementing <see cref="ISynapseHost"/>.
/// Compiles synapse source, validates boundaries, and delegates durable lifecycle to <see cref="IUnitDeployment"/>.
/// </summary>
public sealed class UnitDeployer(
    SynapseCompiler compiler,
    IUnitLoader loader,
    Func<NeuronId, ISources> sources,
    Func<NeuronId, IUnitDeployment> deployments) : ISynapseHost
{
    /// <summary>The count of compiled assemblies emitted by this host's compiler.</summary>
    public int Compiles => compiler.Emits;

    /// <summary>The underlying compiler.</summary>
    public SynapseCompiler Compiler => compiler;

    /// <inheritdoc />
    public async Task<DeployOutcome> Deploy(
        BrainId brain,
        SynapseSource source,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> bindingsPerAlias,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        var build = compiler.Compile(source);
        if (!build.Success)
        {
            return new DeployOutcome(DeployStatus.Failed, null, build.Diagnostics, new Dictionary<string, InstallOutcome>(), "compilation failed");
        }

        if (SynapseCompiler.Verify(build) is { } outside)
        {
            var reason = $"boundary: {outside}";
            return new DeployOutcome(DeployStatus.Refused, build.Hash, [.. build.Diagnostics, new SynapseDiagnostic(source.SourceKey, "DB0045", 0, reason)], new Dictionary<string, InstallOutcome>(), reason);
        }

        var image = new BuildImage(new BuildRef(source.SourceKey, build.Hash), build.AssemblyName, build.Assembly, build.Symbols);
        UnitHandle handle;
        try
        {
            handle = loader.Acquire(image);
        }
        catch (UnitRefusedException ex)
        {
            var reason = $"'{ex.Disallowed}' is outside the surface a synapse assembly may reference";
            return new DeployOutcome(DeployStatus.Refused, build.Hash, [.. build.Diagnostics, new SynapseDiagnostic(source.SourceKey, "DB0045", 0, reason)], new Dictionary<string, InstallOutcome>(), ex.Disallowed);
        }

        using (handle)
        {
            var aliases = handle.Catalog.States.Keys.ToList();
            var needsHashes = aliases.ToDictionary(a => a, a => handle.Catalog.NeedsOf(a).Hash(), StringComparer.Ordinal);
            var storedBuild = new StoredBuild(image, source.Files, source.Icon, aliases, needsHashes);

            var effectiveBindings = bindingsPerAlias != null && bindingsPerAlias.Count > 0
                ? bindingsPerAlias
                : (source.Bindings ?? new Dictionary<string, IReadOnlyDictionary<string, string>>());

            var deployment = deployments(new NeuronId(brain, "synapse.deployment", source.SourceKey));
            var outcome = await deployment.Propose(storedBuild, effectiveBindings, ct).ConfigureAwait(true);
            return outcome with { Diagnostics = build.Diagnostics };
        }
    }

    /// <inheritdoc />
    public async Task<DeployOutcome> Retire(BrainId brain, string sourceKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);

        var sourcesNeuron = sources(new NeuronId(brain, "synapse.sources", "default"));
        var keys = await sourcesNeuron.Keys(ct).ConfigureAwait(true);
        if (!keys.Contains(sourceKey, StringComparer.Ordinal))
        {
            return new DeployOutcome(DeployStatus.Refused, null, [], new Dictionary<string, InstallOutcome>(), $"Unknown source key '{sourceKey}'.");
        }

        var deployment = deployments(new NeuronId(brain, "synapse.deployment", sourceKey));
        var liveRef = await deployment.Ref(UnitView.Live, ct).ConfigureAwait(true);
        await deployment.Retire(ct).ConfigureAwait(true);
        return new DeployOutcome(DeployStatus.Retired, liveRef?.Hash, [], new Dictionary<string, InstallOutcome>(), null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeployedSource>> List(BrainId brain, CancellationToken ct)
    {
        var sourcesNeuron = sources(new NeuronId(brain, "synapse.sources", "default"));
        var keys = await sourcesNeuron.Keys(ct).ConfigureAwait(true);
        var result = new List<DeployedSource>();
        foreach (var key in keys)
        {
            var deployment = deployments(new NeuronId(brain, "synapse.deployment", key));
            var src = await deployment.Source(ct).ConfigureAwait(true);
            if (src is null) { continue; }
            var stagedRef = await deployment.Ref(UnitView.Proposed, ct).ConfigureAwait(true);
            var isStaged = stagedRef is not null && !string.Equals(stagedRef.Value.Hash, src.Hash, StringComparison.Ordinal);
            var status = src.RebuildReason is not null
                ? DeployStatus.RebuildRequired
                : (isStaged ? DeployStatus.Staged : DeployStatus.Deployed);
            result.Add(new DeployedSource(
                src.SourceKey,
                src.Hash,
                src.Aliases,
                status,
                isStaged ? stagedRef!.Value.Hash : null,
                src.DeployedAt,
                src.RebuildReason));
        }
        return [.. result.OrderBy(s => s.SourceKey, StringComparer.Ordinal)];
    }

    /// <summary>Returns the deployed source view of a live build.</summary>
    public async Task<UnitSourceView?> Source(BrainId brain, string sourceKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        var deployment = deployments(new NeuronId(brain, "synapse.deployment", sourceKey));
        return await deployment.Source(ct).ConfigureAwait(true);
    }
}
