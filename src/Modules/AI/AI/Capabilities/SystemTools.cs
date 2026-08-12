using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;

using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.AI;

// The assistant's whole surface: three constant tools, no matter how many
// modules exist. find → get → fire, with correctable errors the model can act on.
public sealed class SystemTools(
    IGrainFactory grains,
    OwnerId owner,
    IServiceProvider services,
    ActorContext? verifiedActor = null)
{
    public const string FindCapabilities = "find_capabilities";
    public const string GetNeurons = "get_neurons";
    public const string Fire = "fire";

    private const int FindLimit = 8;

    // In-process replies land in milliseconds; a long wait only slows the
    // model's self-correction when a target refuses or is unconfigured.
    private static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(15);

    // Long enough for the 50ms outbox drain to attempt the hop and report a settled
    // refusal, short enough that firing a fact still feels immediate.
    private static readonly TimeSpan FactOutcomeWait = TimeSpan.FromSeconds(2);

    public IReadOnlyList<AIFunction> All()
        =>
        [
            AIFunctionFactory.Create(FindCapabilitiesAsync, FindCapabilities,
                "Search the system's contracts for what can be done. Returns requests you can fire (with their signatures) and facts you can route with db.connect."),
            AIFunctionFactory.Create(GetNeuronsAsync, GetNeurons,
                "List registered instances (including cold/disabled), live activations (type:owner/name), and synapse-graph connections. Optionally filter by grain type."),
            AIFunctionFactory.Create(FireAsync, Fire,
                "Send a request synapse and return its reply. 'contract' is a contract id from find_capabilities; 'arguments' are its fields (commandId is filled for you); 'target' overrides the default instance — a grain type ('timer'), an instance name ('main'), or type:name."),
        ];

    private async Task<string> FindCapabilitiesAsync(string intent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(intent))
        {
            return "Say what you are trying to do, in a few words.";
        }

        var index = services.GetService<CapabilityIndex>();
        if (index is null)
        {
            return "The capability index is not available in this deployment.";
        }

        var hits = await index.FindAsync(
            intent,
            FindLimit,
            services.GetService<IEmbeddingGenerator<string, Embedding<float>>>(),
            cancellationToken).ConfigureAwait(false);

        var lines = new StringBuilder();
        AppendExternalServers(lines);
        lines.AppendLine(
            "Library (Wave 7): fire db.discover-library with this intent, then "
            + "db.install-library-artifact / db.enable-library-install for principal-local copies.");
        lines.AppendLine(
            "Behaviors (Wave 8): fire db.start-repo-review on behavior:main with RootPath + Intent.");

        if (hits.Count == 0)
        {
            lines.AppendLine("No compiled contract matches. Try library discover or different words.");
            return lines.ToString();
        }

        foreach (var hit in hits)
        {
            if (hit.Kind == CapabilityHit.RequestKind)
            {
                lines.AppendLine(
                    $"{hit.ContractId} — request handled by '{hit.NeuronContractId}' "
                    + $"(default target instance '{hit.DefaultInstanceName}')");
                lines.AppendLine($"  {hit.Signature}");
                continue;
            }

            lines.AppendLine($"{hit.ContractId} — fact; route it with db.connect, then trigger its source");
            lines.AppendLine($"  {hit.Signature}");
        }

        return lines.ToString();
    }

    private void AppendExternalServers(StringBuilder lines)
    {
        var servers = services.GetServices<ExternalServerCapability>().ToArray();
        if (servers.Length == 0)
        {
            return;
        }

        lines.AppendLine(
            "External servers (their live tool catalogs are the capabilities — fire "
            + "db.mcp.list-tools at mcp:<key>, then db.mcp.call-tool):");
        foreach (var server in servers)
        {
            lines.AppendLine($"  {server.Key} ({server.DisplayName}) — target mcp:{server.Key}");
        }
    }

    // Absence is stated rather than left blank: a wire made before provenance existed is not
    // the same as a wire whose author declined to say why.
    private static string DescribeProvenance(Provenance? provenance)
        => provenance is null
            ? "intent: unrecorded — wired before provenance was kept"
            : string.IsNullOrEmpty(provenance.StatedIntent)
                ? $"intent: none stated — wired by {provenance.Author} at {provenance.At:u}"
                : $"intent: \"{provenance.StatedIntent}\" — wired by {provenance.Author} at {provenance.At:u}";

    private async Task<string> GetNeuronsAsync(CancellationToken cancellationToken, string? grainType = null)
    {
        var activated = await ActivatedAsync(cancellationToken).ConfigureAwait(false);
        var liveFilter = string.IsNullOrWhiteSpace(grainType)
            ? activated
            : [.. activated.Where(neuron => string.Equals(neuron.Type, grainType.Trim(), StringComparison.OrdinalIgnoreCase))];

        var lines = new StringBuilder();

        // Registry is durable: cold charts, idle schedules, disabled bundle members.
        RegisteredInstance[] registered = [];
        try
        {
            var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(owner).ToGrainId());
            var registryId = VerifiedActor.Current is { } actor
                ? IRegistry.ForPrincipal(owner, actor.PrincipalId)
                : IRegistry.ForOwner(owner);
            var opened = await session
                .ReadNeuronJournal(ISessionNeuron.ForOwner(owner), JournalKind.Incoming, long.MaxValue)
                .ConfigureAwait(false);
            var cursor = opened.ResumeSequence;
            var listRequest = new ListInstances(CommandId.New());
            var delivery = await session.Fire(registryId, listRequest).ConfigureAwait(false);
            var abandon = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);
            while (DateTimeOffset.UtcNow < abandon)
            {
                var page = await session
                    .ReadNeuronJournal(ISessionNeuron.ForOwner(owner), JournalKind.Incoming, cursor)
                    .ConfigureAwait(false);
                foreach (var entry in page.Delta)
                {
                    if (entry.CorrelationId == delivery.CorrelationId
                        && entry.Synapse is InstancesListed listed)
                    {
                        registered = listed.Items;
                        goto RegistryLoaded;
                    }
                }

                cursor = page.ResumeSequence;
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Registry unavailable — live list still works.
        }

    RegistryLoaded:

        if (registered.Length > 0)
        {
            RegisteredInstance[] filtered = string.IsNullOrWhiteSpace(grainType)
                ? registered
                : [.. registered.Where(entry =>
                    string.Equals(entry.Subject.Type, grainType.Trim(), StringComparison.OrdinalIgnoreCase))];

            lines.AppendLine(filtered.Length == 0
                ? "Registered instances: (none match filter)"
                : "Registered instances (exist even when cold):");
            var liveKeys = activated.Select(static n => n.ToString()).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in filtered.OrderBy(static e => e.Subject.ToString(), StringComparer.Ordinal))
            {
                var heat = liveKeys.Contains(entry.Subject.ToString()) ? "live" : "cold";
                var enabled = entry.Enabled ? "enabled" : "disabled";
                var bundle = entry.Bundle is null ? "" : $" bundle={entry.Bundle}";
                var note = entry.Note is null ? "" : $" note={entry.Note}";
                lines.AppendLine(
                    $"  {entry.Subject} role={entry.Role} [{heat}, {enabled}]{bundle}{note}");
            }
        }
        else
        {
            lines.AppendLine(
                "Registered instances: (none — fire db.register-instance or db.install-bundle)");
        }

        lines.AppendLine(liveFilter.Count == 0 ? "Live (activated) instances: (none match)" : "Live (activated) instances:");
        foreach (var neuron in liveFilter)
        {
            lines.AppendLine($"  {neuron}");
        }

        var connections = await grains
            .GetGrain<ISynapseGraph>(
                (VerifiedActor.Current is { } graphActor
                    ? ISynapseGraph.ForPrincipal(owner, graphActor.PrincipalId)
                    : ISynapseGraph.ForOwner(owner)).ToGrainId())
            .Connections()
            .WaitAsync(DeliveryPolicy.ConnectionLookupTimeout, cancellationToken).ConfigureAwait(false);
        lines.AppendLine(connections.Count == 0 ? "No connections yet." : "Connections:");
        foreach (var connection in connections)
        {
            var transform = connection.Transform is null ? "" : $" via {connection.Transform}";
            // The id is printed because db.disconnect addresses it: without it the model can
            // only unwire what it created in the same turn.
            lines.AppendLine($"  [{connection.ConnectionId}] {connection.Source} "
                + $"--{connection.SynapseAlias}--> {connection.Target}{transform}");
            lines.AppendLine($"      {DescribeProvenance(connection.Provenance)}");
        }

        return lines.ToString();
    }

    private async Task<string> FireAsync(
        string contract,
        CancellationToken cancellationToken,
        JsonElement? arguments = null,
        string? target = null)
    {
        try
        {
            return await FireCoreAsync(contract, arguments, target, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The model must always see WHY, never an opaque "Function failed".
            return $"fire('{contract}') failed: {failure.Message}";
        }
    }

    private async Task<string> FireCoreAsync(
        string contract,
        JsonElement? arguments,
        string? target,
        CancellationToken cancellationToken)
    {
        var catalog = services.GetService<ActiveCapabilityCatalog>();
        var typeMap = services.GetService<ActiveModuleContractTypeMap>();
        if (catalog is null || typeMap is null)
        {
            return "The capability catalog is not available in this deployment.";
        }

        if (string.IsNullOrWhiteSpace(contract))
        {
            return "Name the contract to fire; find_capabilities lists them.";
        }

        var host = HostOf(catalog, contract.Trim());
        if (host is null)
        {
            return NoSuchContract(contract);
        }

        if (!typeMap.TryGetNeuronGrainType(host.ContractId, out var grainType) || grainType is null)
        {
            return $"'{contract}' has no reachable neuron in this deployment.";
        }

        var requestType = SynapseTypeIndex.FindByAlias(contract.Trim());
        if (requestType is null)
        {
            return NoSuchContract(contract);
        }

        Synapse request;
        try
        {
            request = SynapseCapabilityTool.BindModelArguments(
                requestType, contract, ArgumentPairs(arguments), owner);
            // Verified principal from the chat/HTTP boundary overwrites any model Actor.
            request = SynapseCapabilityTool.StampVerifiedActor(request, verifiedActor);
        }
        catch (Exception invalid) when (invalid is JsonException or InvalidOperationException or ArgumentException)
        {
            return $"The arguments do not fit {ContractSignature.Of(requestType)}: {invalid.Message}";
        }

        var activated = await ActivatedAsync(cancellationToken).ConfigureAwait(false);
        if (ResolveTarget(target, grainType, host.DefaultInstanceName, typeMap, activated) is not { } resolved)
        {
            return $"Target '{target}' names no known neuron. get_neurons lists the live instances.";
        }

        if (!string.Equals(resolved.Type, grainType, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{contract}' is handled by '{grainType}', not by '{resolved}'. Omit target "
                + $"to reach {grainType}:{host.DefaultInstanceName}; connection endpoints belong "
                + "in the arguments (source/target), not in fire's target.";
        }

        if (GuessedIdentity(request, activated, catalog, typeMap) is { } guessed)
        {
            return guessed;
        }

        return await SendAsync(request, requestType, resolved, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> SendAsync(
        Synapse request,
        Type requestType,
        NeuronId target,
        CancellationToken cancellationToken)
    {
        var sessionId = ISessionNeuron.ForOwner(owner);
        var session = grains.GetGrain<ISessionNeuron>(sessionId.ToGrainId());

        var responseType = ReplyTypeOf(requestType);
        var opened = await session
            .ReadNeuronJournal(sessionId, JournalKind.Incoming, long.MaxValue)
            .ConfigureAwait(false);
        var cursor = opened.ResumeSequence;
        var delivery = await session.Fire(target, request).ConfigureAwait(false);

        // A plain fact has no reply to wait for, but the hop has not been attempted yet either:
        // the outbox drains after this call returns. Waiting briefly turns a settled refusal
        // into a reason instead of a confident "Delivered" the model would repeat to the owner.
        var abandonAfter = DateTimeOffset.UtcNow + (responseType is null ? FactOutcomeWait : ReplyWait);
        while (true)
        {
            var read = await session
                .ReadNeuronJournal(sessionId, JournalKind.Incoming, cursor)
                .ConfigureAwait(false);

            foreach (var journaled in read.Delta)
            {
                if (journaled.Synapse is RouteOutcome outcome
                    && outcome.Correlation == delivery.CorrelationId)
                {
                    return $"{outcome.Kind} by {outcome.Receiver}: {outcome.Reason}";
                }

                if (journaled.Synapse is Unrouted unrouted
                    && unrouted.Correlation == delivery.CorrelationId)
                {
                    return $"Unrouted: nothing is connected to receive '{unrouted.Alias}' from "
                        + $"{unrouted.Source}. Wire it with db.connect first, then trigger the source.";
                }

                if (responseType is not null
                    && journaled.CorrelationId == delivery.CorrelationId
                    && responseType.IsInstanceOfType(journaled.Synapse))
                {
                    return JsonSerializer.Serialize(journaled.Synapse, responseType);
                }
            }

            if (read.ResetSnapshot is not null)
            {
                return $"The session journal compacted past sequence {cursor} before an outcome for "
                    + $"{target} arrived. The request is committed but its result is unknown — say so.";
            }

            cursor = read.ResumeSequence;

            if (DateTimeOffset.UtcNow >= abandonAfter)
            {
                return responseType is null
                    ? $"Committed for delivery to {target}; no failure reported within "
                        + $"{FactOutcomeWait.TotalSeconds:F0}s."
                    : $"No {responseType.Name} reply from {target} within "
                        + $"{ReplyWait.TotalSeconds:F0}s. The request is "
                        + "committed; the target may be unconfigured or refusing — tell the owner what "
                        + "you attempted rather than claiming it worked.";
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
        }
    }

    // A named instance of a type that has live instances — but is none of them and
    // not the default — is almost always a guessed identity (the timer:dev/timer bug).
    private static string? GuessedIdentity(
        Synapse request,
        IReadOnlyList<NeuronId> activated,
        ActiveCapabilityCatalog catalog,
        ActiveModuleContractTypeMap typeMap)
    {
        foreach (var property in request.GetType().GetProperties())
        {
            if (property.PropertyType != typeof(NeuronId)
                || property.GetValue(request) is not NeuronId subject)
            {
                continue;
            }

            var live = activated.Where(neuron => neuron.Type == subject.Type).ToArray();
            if (live.Length == 0
                || live.Any(neuron => neuron.Name == subject.Name)
                || subject.Name == DefaultInstanceOf(subject.Type, catalog, typeMap))
            {
                continue;
            }

            return $"{property.Name} '{subject}' does not exist. Live '{subject.Type}' instances: "
                + string.Join(", ", live.Select(static neuron => neuron.ToString()))
                + ". Use one of these, or the exact instance you mean.";
        }

        return null;
    }

    private static string? DefaultInstanceOf(
        string grainType,
        ActiveCapabilityCatalog catalog,
        ActiveModuleContractTypeMap typeMap)
    {
        foreach (var manifest in catalog.Modules)
        {
            foreach (var neuron in manifest.Neurons)
            {
                if (typeMap.TryGetNeuronGrainType(neuron.ContractId, out var mapped)
                    && string.Equals(mapped, grainType, StringComparison.OrdinalIgnoreCase))
                {
                    return neuron.DefaultInstanceName;
                }
            }
        }

        return null;
    }

    private NeuronId? ResolveTarget(
        string? target,
        string hostGrainType,
        string defaultInstance,
        ActiveModuleContractTypeMap typeMap,
        IReadOnlyList<NeuronId> activated)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return new NeuronId(hostGrainType, owner, defaultInstance);
        }

        var trimmed = target.Trim();
        if (trimmed.Contains(':', StringComparison.Ordinal))
        {
            var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
            var type = trimmed[..separator];
            var rest = trimmed[(separator + 1)..];
            var name = rest.Contains('/', StringComparison.Ordinal)
                ? rest[(rest.IndexOf('/', StringComparison.Ordinal) + 1)..]
                : rest;

            return KnownGrainType(type, typeMap, activated)
                ? new NeuronId(type, owner, name)
                : null;
        }

        return KnownGrainType(trimmed, typeMap, activated)
            ? new NeuronId(trimmed, owner, DefaultInstanceOf(
                trimmed.ToLowerInvariant(),
                services.GetRequiredService<ActiveCapabilityCatalog>(),
                typeMap) ?? "default")
            : new NeuronId(hostGrainType, owner, trimmed);
    }

    private static bool KnownGrainType(
        string candidate,
        ActiveModuleContractTypeMap typeMap,
        IReadOnlyList<NeuronId> activated)
        => typeMap.KnownGrainTypes.Contains(candidate.ToLowerInvariant())
            || activated.Any(neuron => string.Equals(neuron.Type, candidate, StringComparison.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<NeuronId>> ActivatedAsync(CancellationToken cancellationToken)
    {
        var statistics = await grains
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics()
            .WaitAsync(cancellationToken).ConfigureAwait(false);

        var ownerPrefix = $"{owner.Value}/";
        var live = new List<NeuronId>();

        foreach (var statistic in statistics)
        {
            var key = statistic.GrainId.Key.ToString();
            if (key is null || !key.StartsWith(ownerPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            live.Add(new NeuronId(
                statistic.GrainId.Type.ToString()!,
                owner,
                key[ownerPrefix.Length..]));
        }

        return live;
    }

    private static NeuronCapabilityDescriptor? HostOf(ActiveCapabilityCatalog catalog, string contract)
    {
        foreach (var manifest in catalog.Modules)
        {
            foreach (var neuron in manifest.Neurons)
            {
                if (neuron.Accepted.Any(accepted =>
                        string.Equals(accepted.ContractId, contract, StringComparison.Ordinal)))
                {
                    return neuron;
                }
            }
        }

        return null;
    }

    private string NoSuchContract(string contract)
    {
        var near = services.GetService<CapabilityIndex>()?.Find(contract, 5) ?? [];
        var suggestion = near.Count == 0
            ? "find_capabilities can search for it."
            : "Did you mean: " + string.Join(", ", near.Select(static hit => hit.ContractId)) + "?";

        return $"No fireable contract '{contract}'. {suggestion}";
    }

    private static IEnumerable<KeyValuePair<string, object?>> ArgumentPairs(JsonElement? arguments)
    {
        if (arguments is not { ValueKind: JsonValueKind.Object } bound)
        {
            yield break;
        }

        foreach (var property in bound.EnumerateObject())
        {
            yield return new KeyValuePair<string, object?>(property.Name, property.Value);
        }
    }

    private static Type? ReplyTypeOf(Type requestType)
    {
        for (var probed = requestType.BaseType; probed is not null; probed = probed.BaseType)
        {
            if (probed.IsGenericType && probed.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                return probed.GenericTypeArguments[0];
            }
        }

        return null;
    }
}
