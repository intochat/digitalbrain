using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;

using DigitalBrain.Core;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;

namespace DigitalBrain.AI;

// The assistant's whole surface: five constant tools, no matter how many
// modules exist. find → get → connect/disconnect/fire, with correctable errors the model can act on.
public sealed class SystemTools(
    IGrainFactory grains,
    OwnerId owner,
    IServiceProvider services,
    ActorContext? verifiedActor = null)
{
    public const string FindCapabilities = "find_capabilities";
    public const string GetNeurons = "get_neurons";
    public const string Fire = "fire";
    public const string BrainConnect = "brain_connect";
    public const string BrainDisconnect = "brain_disconnect";

    private const int FindLimit = 8;

    // The entity types the UIRenderer's own capabilities write (chart:demo, surface:desk):
    // the only entity-shaped fire targets ResolveTarget silently redirects to their writer.
    private static readonly string[] RendererEntityGrainTypes = ["chart", "surface"];

    // In-process replies land in milliseconds; a long wait only slows the
    // model's self-correction when a target refuses or is unconfigured.
    private static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(15);

    public IReadOnlyList<AIFunction> All()
        =>
        [
            AIFunctionFactory.Create(FindCapabilitiesAsync, FindCapabilities,
                "Search the system's contracts for what can be done. Returns requests you can fire (with their signatures) and facts you can route with brain_connect."),
            AIFunctionFactory.Create(GetNeuronsAsync, GetNeurons,
                "List the brain's registered nodes (including cold ones), live activations (type:owner/name), and connections. Optionally filter by grain type."),
            AIFunctionFactory.Create(FireAsync, Fire,
                "Send a request synapse and return its reply. 'contract' is a contract id from find_capabilities; 'arguments' are its fields (commandId is filled for you); 'target' overrides the default instance — a grain type ('timer'), an instance name ('main'), or type:name."),
            AIFunctionFactory.Create(BrainConnectAsync, BrainConnect,
                "Wire source → target in the owner's brain: facts the source emits under synapseAlias are delivered to the target. source and target are 'type:name' instances from get_neurons; synapseAlias is the fact's contract id from find_capabilities."),
            AIFunctionFactory.Create(BrainDisconnectAsync, BrainDisconnect,
                "Remove a wire from the owner's brain: the exact source, synapseAlias, and target of an existing connection, as brain_connect wired it."),
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

            lines.AppendLine($"{hit.ContractId} — fact; route it with brain_connect, then trigger its source");
            lines.AppendLine($"  {hit.Signature}");
        }

        return lines.ToString();
    }

    private async Task<string> GetNeuronsAsync(CancellationToken cancellationToken, string? grainType = null)
    {
        var activated = await ActivatedAsync(cancellationToken).ConfigureAwait(false);
        var liveFilter = string.IsNullOrWhiteSpace(grainType)
            ? activated
            : [.. activated.Where(neuron => string.Equals(neuron.Type, grainType.Trim(), StringComparison.OrdinalIgnoreCase))];

        var lines = new StringBuilder();

        // The brain's registry is durable: it remembers nodes across activations, cold or hot.
        BrainState? brainState = null;
        var brainUnreachable = false;
        try
        {
            brainState = await OwnersBrain()
                .Read()
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Emptiness must never be claimed for an unreachable brain; say so instead.
            brainUnreachable = true;
        }

        var nodes = brainState?.Nodes ?? [];
        IReadOnlyList<BrainReference> filteredNodes = string.IsNullOrWhiteSpace(grainType)
            ? nodes
            : [.. nodes.Where(node =>
                string.Equals(node.Type, grainType.Trim(), StringComparison.OrdinalIgnoreCase))];

        if (brainUnreachable)
        {
            lines.AppendLine(
                "Brain-registered nodes: (the brain was unreachable — registered nodes are unknown)");
        }
        else if (filteredNodes.Count > 0)
        {
            lines.AppendLine("Brain-registered nodes (exist even when cold):");
            foreach (var node in filteredNodes.OrderBy(static n => n.Key, StringComparer.Ordinal))
            {
                var heat = activated.Any(neuron =>
                    string.Equals(neuron.Type, node.Type, StringComparison.Ordinal)
                    && string.Equals(neuron.Name, node.Name, StringComparison.Ordinal))
                    ? "live"
                    : "cold";
                lines.AppendLine($"  {node.Type}:{node.Name} kind={node.Kind} [{heat}] lastUsed={node.LastUsed:u}");
            }
        }
        else
        {
            lines.AppendLine(
                "Brain-registered nodes: (none yet — neurons register themselves on first activation)");
        }

        lines.AppendLine(liveFilter.Count == 0 ? "Live (activated) instances: (none match)" : "Live (activated) instances:");
        foreach (var neuron in liveFilter)
        {
            lines.AppendLine($"  {neuron}");
        }

        if (brainUnreachable)
        {
            lines.AppendLine("Connections: (the brain was unreachable — connections are unknown)");
        }
        else
        {
            var connections = brainState?.Connections ?? [];
            lines.AppendLine(connections.Count == 0 ? "No connections yet." : "Connections:");
            foreach (var connection in connections)
            {
                lines.AppendLine($"  {connection.From} --{connection.Role}--> {connection.To}");
            }
        }

        return lines.ToString();
    }

    private async Task<string> BrainConnectAsync(
        string source,
        string synapseAlias,
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(synapseAlias)
                || string.IsNullOrWhiteSpace(target))
            {
                return "brain_connect needs source ('type:name'), synapseAlias (the fact's "
                    + "contract id), and target ('type:name').";
            }

            if (!NeuronId.TryParseInstance(source, owner, out var from))
            {
                return $"Source '{source}' must be written type:name, for example 'timer:default' — no owner segment.";
            }

            if (!NeuronId.TryParseInstance(target, owner, out var to))
            {
                return $"Target '{target}' must be written type:name, for example 'chat:main' — no owner segment.";
            }

            var alias = synapseAlias.Trim();
            await OwnersBrain()
                .Connect(new Connection(from, alias, to))
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken).ConfigureAwait(false);
            return $"Connected {from} --{alias}--> {to}.";
        }
        catch (Exception refused) when (refused is not OperationCanceledException)
        {
            // The model must always see WHY, never an opaque "Function failed".
            return $"brain_connect failed: {refused.Message}";
        }
    }

    private async Task<string> BrainDisconnectAsync(
        string source,
        string synapseAlias,
        string target,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(source)
                || string.IsNullOrWhiteSpace(synapseAlias)
                || string.IsNullOrWhiteSpace(target))
            {
                return "brain_disconnect needs the wire's source ('type:name'), synapseAlias, "
                    + "and target ('type:name') — get_neurons lists the connections.";
            }

            if (!NeuronId.TryParseInstance(source, owner, out var from))
            {
                return $"Source '{source}' must be written type:name, for example 'timer:default' — no owner segment.";
            }

            if (!NeuronId.TryParseInstance(target, owner, out var to))
            {
                return $"Target '{target}' must be written type:name, for example 'chat:main' — no owner segment.";
            }

            var alias = synapseAlias.Trim();
            var routed = await OwnersBrain()
                .Connections(from, alias)
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken).ConfigureAwait(false);
            if (!routed.Any(existing => existing.To == to))
            {
                return $"No wire {from} --{alias}--> {to} exists.";
            }

            await OwnersBrain()
                .Disconnect(new Connection(from, alias, to))
                .WaitAsync(NeuronCallTimeouts.LookupBound, cancellationToken).ConfigureAwait(false);
            return $"Disconnected {from} --{alias}--> {to}.";
        }
        catch (Exception refused) when (refused is not OperationCanceledException)
        {
            // The model must always see WHY, never an opaque "Function failed".
            return $"brain_disconnect failed: {refused.Message}";
        }
    }

    private IBrain OwnersBrain()
        => grains.GetGrain<IBrain>(
            EntityId.For<IBrain>(owner, DigitalBrainNames.DefaultBrain).ToGrainId());

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

        // A capability can declare its own default instance (SynapseCapabilityDescriptor),
        // overriding the host neuron's default — one neuron contract can serve several
        // capabilities that do not all want the same default (uirenderer's own default is
        // "default", but ui.open-surface's is "desk", the surface the shell watches).
        var defaultInstance = host.Accepted
            .FirstOrDefault(accepted => string.Equals(accepted.ContractId, contract.Trim(), StringComparison.Ordinal))
            ?.DefaultInstanceName
            ?? host.DefaultInstanceName;

        var activated = await ActivatedAsync(cancellationToken).ConfigureAwait(false);
        if (ResolveTarget(target, grainType, defaultInstance, typeMap, activated) is not { } resolved)
        {
            return $"Target '{target}' names no known neuron. get_neurons lists the live instances.";
        }

        if (!string.Equals(resolved.Type, grainType, StringComparison.OrdinalIgnoreCase))
        {
            return $"'{contract}' is handled by '{grainType}', not by '{resolved}'. Omit target "
                + $"to reach {grainType}:{defaultInstance}; connection endpoints belong "
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

        if (responseType is null)
        {
            // A send is a direct awaited grain call: by the time Fire returns, the target's
            // turn has completed — a refusal would have thrown out of Fire itself.
            return $"Delivered to {target}.";
        }

        // The reply rides an unawaited call back into the session, so it can land moments
        // after Fire returns; await it through the session journal as the client does.
        var abandonAfter = DateTimeOffset.UtcNow + ReplyWait;
        while (true)
        {
            var read = await session
                .ReadNeuronJournal(sessionId, JournalKind.Incoming, cursor)
                .ConfigureAwait(false);

            foreach (var journaled in read.Delta)
            {
                if (journaled.CorrelationId == delivery.CorrelationId
                    && responseType.IsInstanceOfType(journaled.Synapse))
                {
                    return JsonSerializer.Serialize(journaled.Synapse, responseType);
                }
            }

            if (read.ResetSnapshot is not null)
            {
                return $"The session journal compacted past sequence {cursor} before a reply from "
                    + $"{target} arrived. The request was delivered but its reply is unknown — say so.";
            }

            cursor = read.ResumeSequence;

            if (DateTimeOffset.UtcNow >= abandonAfter)
            {
                return $"No {responseType.Name} reply from {target} within "
                    + $"{ReplyWait.TotalSeconds:F0}s. The request was "
                    + "delivered; the target may be unconfigured or refusing — tell the owner what "
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

            // An entity is never a delivery target: an entity-shaped target ('chart:demo')
            // routes to the handling neuron, and the entity's name selects the writer
            // instance (chart:demo → uirenderer:demo, which fills chart:demo). Only the
            // renderer's own entities redirect this way — any other entity-shaped target
            // (e.g. 'brain:x') is not one this host can fill, so it falls through to the
            // type-mismatch refusal below instead of a silent adopt.
            if (!string.Equals(type, hostGrainType, StringComparison.OrdinalIgnoreCase)
                && RendererEntityGrainTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
                && typeMap.KnownEntityGrainTypes.Contains(type.ToLowerInvariant()))
            {
                return new NeuronId(hostGrainType, owner, name);
            }

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
