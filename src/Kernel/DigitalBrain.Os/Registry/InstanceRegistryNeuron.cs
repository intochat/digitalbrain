using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Os;

[GrainType(IRegistry.GrainTypeName)]
public sealed class InstanceRegistryNeuron : Neuron, IRegistry
{
    private const string StateName = "registry.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<RegistryState> _states;

    public InstanceRegistryNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<RegistryState>>();
    }

    public Task HandleAsync(ListInstances synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        return ReplyAsync(new InstancesListed(synapse.CommandId, LoadAll()), cancellationToken);
    }

    public Task HandleAsync(RegisterInstance synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        RequireOwned(synapse.Subject);

        if (string.IsNullOrWhiteSpace(synapse.Role))
        {
            throw new NeuronAuthorizationException(
                $"Registry '{Id}' refuses a registration without a role "
                + "(e.g. chart, timer, schedule, cell).");
        }

        var record = new RegisteredInstance(
            synapse.Subject,
            synapse.Role.Trim(),
            string.IsNullOrWhiteSpace(synapse.Bundle) ? null : synapse.Bundle.Trim(),
            synapse.Enabled,
            TimeProvider.GetUtcNow(),
            string.IsNullOrWhiteSpace(synapse.Note) ? null : synapse.Note.Trim());

        Upsert(record);
        return ReplyAsync(new InstanceRegistered(synapse.CommandId, record), cancellationToken);
    }

    public Task HandleAsync(RetireInstance synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        RequireOwned(synapse.Subject);

        if (!Remove(synapse.Subject))
        {
            throw new NeuronAuthorizationException(
                $"Registry '{Id}' has no instance '{synapse.Subject}' to retire.");
        }

        return ReplyAsync(new InstanceRetired(synapse.CommandId, synapse.Subject), cancellationToken);
    }

    public Task HandleAsync(SetInstanceEnabled synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        RequireOwned(synapse.Subject);

        var current = Find(synapse.Subject)
            ?? throw new NeuronAuthorizationException(
                $"Registry '{Id}' has no instance '{synapse.Subject}' to enable/disable.");

        var updated = current with { Enabled = synapse.Enabled };
        Upsert(updated);
        return ReplyAsync(new InstanceEnabledChanged(synapse.CommandId, updated), cancellationToken);
    }

    public async Task HandleAsync(InstallBundle synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        if (string.IsNullOrWhiteSpace(synapse.Name))
        {
            throw new NeuronAuthorizationException($"Registry '{Id}' refuses a bundle without a name.");
        }

        if (synapse.Members is null || synapse.Members.Length == 0)
        {
            throw new NeuronAuthorizationException(
                $"Registry '{Id}' refuses bundle '{synapse.Name}' with no members.");
        }

        var bundleName = synapse.Name.Trim();
        var registeredAt = TimeProvider.GetUtcNow();

        foreach (var member in synapse.Members)
        {
            if (string.IsNullOrWhiteSpace(member.GrainType) || string.IsNullOrWhiteSpace(member.Name))
            {
                throw new NeuronAuthorizationException(
                    $"Registry '{Id}' refuses a bundle member without grainType and name.");
            }

            if (string.IsNullOrWhiteSpace(member.Role))
            {
                throw new NeuronAuthorizationException(
                    $"Registry '{Id}' refuses bundle member '{member.GrainType}:{member.Name}' without a role.");
            }

            var subject = new NeuronId(member.GrainType.Trim(), Id.Owner, member.Name.Trim());
            Upsert(new RegisteredInstance(
                subject,
                member.Role.Trim(),
                bundleName,
                Enabled: false,
                registeredAt,
                string.IsNullOrWhiteSpace(member.Note) ? $"bundle:{bundleName}" : member.Note.Trim()));
        }

        var wireCount = 0;
        if (synapse.Wires is { Length: > 0 })
        {
            var graph = PrincipalGraph.Resolve(Id.Owner);
            var intent = string.IsNullOrWhiteSpace(synapse.Intent)
                ? $"bundle install '{bundleName}' (disabled)"
                : synapse.Intent.Trim();

            foreach (var wire in synapse.Wires)
            {
                var source = new NeuronId(wire.SourceType.Trim(), Id.Owner, wire.SourceName.Trim());
                var target = new NeuronId(wire.TargetType.Trim(), Id.Owner, wire.TargetName.Trim());
                var connectionId = StableConnectionId(bundleName, source, wire.SynapseAlias, target);
                await SendAsync(
                        graph,
                        new Connect(
                            connectionId,
                            source,
                            wire.SynapseAlias.Trim(),
                            target,
                            wire.Transform,
                            ExpiresAt: null,
                            Intent: intent))
                    .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
                wireCount++;
            }

            await FlushOutboxAsync(cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await ReplyAsync(
                new BundleInstalled(
                    synapse.CommandId,
                    bundleName,
                    synapse.Members.Length,
                    wireCount,
                    Enabled: false),
                cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private RegisteredInstance[] LoadAll()
        => Load().Instances;

    private RegistryState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : RegistryState.Empty;

    private void Stage(RegistryState state)
        => _state.Value = _states.SerializeToArray(state);

    private RegisteredInstance? Find(NeuronId subject)
        => LoadAll().FirstOrDefault(entry => entry.Subject == subject);

    private void Upsert(RegisteredInstance record)
    {
        var all = LoadAll().ToList();
        var index = all.FindIndex(entry => entry.Subject == record.Subject);
        if (index >= 0)
        {
            all[index] = record;
        }
        else
        {
            all.Add(record);
        }

        Stage(new RegistryState([.. all]));
    }

    private bool Remove(NeuronId subject)
    {
        var all = LoadAll().ToList();
        var removed = all.RemoveAll(entry => entry.Subject == subject) > 0;
        if (removed)
        {
            Stage(new RegistryState([.. all]));
        }

        return removed;
    }

    private void RequireOwned(NeuronId subject)
    {
        if (subject.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Registry '{Id}' cannot manage '{subject}' owned by '{subject.Owner}'.");
        }
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A registry command requires a command id.");
        }
    }

    private static Guid StableConnectionId(
        string bundleName,
        NeuronId source,
        string alias,
        NeuronId target)
    {
        var key = $"bundle:{bundleName}|{source}|{alias}|{target}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }
}
