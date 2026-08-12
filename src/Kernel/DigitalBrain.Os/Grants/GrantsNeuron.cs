using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Os;

[GrainType(IGrants.GrainTypeName)]
public sealed class GrantsNeuron : Neuron, IGrants
{
    private const string StateName = "grants.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<GrantsState> _states;

    public GrantsNeuron()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _states = ServiceProvider.GetRequiredService<Serializer<GrantsState>>();
    }

    public Task HandleAsync(GrantAccess synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var grantor = VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Grants '{Id}' refuses grant without a verified principal.");

        if (synapse.Subject.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Grants '{Id}' cannot grant access to '{synapse.Subject}' owned by '{synapse.Subject.Owner}'.");
        }

        if (!CanGrantSubject(grantor.PrincipalId, synapse.Subject))
        {
            throw new NeuronAuthorizationException(
                $"Grants '{Id}' refuses grant of '{synapse.Subject}': only the principal who owns "
                + "that instance name may grant access (library catalog grants use owner grants book).");
        }

        if (synapse.Grantee == grantor.PrincipalId)
        {
            throw new NeuronAuthorizationException(
                $"Grants '{Id}' refuses a grant to yourself.");
        }

        var record = new GrantRecord(
            synapse.Grantee,
            synapse.Subject,
            synapse.Kind,
            grantor.PrincipalId,
            TimeProvider.GetUtcNow(),
            string.IsNullOrWhiteSpace(synapse.Intent) ? null : synapse.Intent.Trim());

        Upsert(record);
        return ReplyAsync(new AccessGranted(synapse.CommandId, record), cancellationToken);
    }

    public Task HandleAsync(RevokeAccess synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);

        var grantor = VerifiedActor.Current
            ?? throw new NeuronAuthorizationException(
                $"Grants '{Id}' refuses revoke without a verified principal.");

        if (!Remove(synapse.Grantee, synapse.Subject, synapse.Kind, grantor.PrincipalId))
        {
            throw new NeuronAuthorizationException(
                $"Grants '{Id}' has no {synapse.Kind} grant of '{synapse.Subject}' "
                + $"to '{synapse.Grantee}' to revoke.");
        }

        return ReplyAsync(
            new AccessRevoked(synapse.CommandId, synapse.Grantee, synapse.Subject, synapse.Kind),
            cancellationToken);
    }

    public Task HandleAsync(ListGrants synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireCommand(synapse.CommandId);
        return ReplyAsync(new GrantsListed(synapse.CommandId, LoadAll()), cancellationToken);
    }

    public Task<bool> HasAccess(PrincipalId grantee, NeuronId subject, GrantKind kind)
        => Task.FromResult(
            LoadAll().Any(g =>
                g.Grantee == grantee
                && g.Subject == subject
                && g.Kind == kind));

    // Access check used by chart/journal reads. Ownership wins; else a live grant is required.
    public static async Task RequireReadAccessAsync(
        IGrainFactory grains,
        NeuronId subject,
        CancellationToken cancellationToken)
    {
        var actor = VerifiedActor.Current;
        if (actor is null)
        {
            // Unattributed system turns may touch owner-scoped (non-partitioned) names only.
            if (PrincipalPartition.TryParse(subject.Name, out _, out _))
            {
                throw new NeuronAuthorizationException(
                    $"Access to '{subject}' denied: no verified principal on a principal-scoped instance.");
            }

            return;
        }

        if (PrincipalPartition.OwnsInstance(actor.PrincipalId, subject.Name))
        {
            return;
        }

        if (!PrincipalPartition.TryParse(subject.Name, out var subjectPrincipal, out _))
        {
            // Non-partitioned legacy name — allow verified principals under the owner for now.
            return;
        }

        var grantsId = IGrants.ForPrincipal(subject.Owner, subjectPrincipal);
        var allowed = await grains
            .GetGrain<IGrants>(grantsId.ToGrainId())
            .HasAccess(actor.PrincipalId, subject, GrantKind.Read)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        if (!allowed)
        {
            throw new NeuronAuthorizationException(
                $"Access to '{subject}' denied for principal '{actor.PrincipalId.Value:N}': "
                + "no ownership and no Read grant. Ask the owner to fire db.grant-access.");
        }
    }

    private GrantRecord[] LoadAll()
        => Load().Grants;

    private GrantsState Load()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : GrantsState.Empty;

    private void Stage(GrantsState state)
        => _state.Value = _states.SerializeToArray(state);

    private void Upsert(GrantRecord record)
    {
        var all = LoadAll().ToList();
        var index = all.FindIndex(g =>
            g.Grantee == record.Grantee
            && g.Subject == record.Subject
            && g.Kind == record.Kind);
        if (index >= 0)
        {
            all[index] = record;
        }
        else
        {
            all.Add(record);
        }

        Stage(new GrantsState([.. all]));
    }

    private bool Remove(
        PrincipalId grantee,
        NeuronId subject,
        GrantKind kind,
        PrincipalId grantor)
    {
        var all = LoadAll().ToList();
        var removed = all.RemoveAll(g =>
            g.Grantee == grantee
            && g.Subject == subject
            && g.Kind == kind
            && g.Grantor == grantor) > 0;
        if (removed)
        {
            Stage(new GrantsState([.. all]));
        }

        return removed;
    }


    // Principal-partitioned subjects: grantor must own the instance name.
    // Library 2b (a): shared owner catalog grants live on IGrants.ForOwner for subject library/main.
    private bool CanGrantSubject(PrincipalId grantor, NeuronId subject)
    {
        if (PrincipalPartition.OwnsInstance(grantor, subject.Name))
        {
            return true;
        }

        return string.Equals(Id.Name, IGrants.InstanceName, StringComparison.Ordinal)
            && string.Equals(subject.Type, ILibrary.GrainTypeName, StringComparison.Ordinal)
            && string.Equals(subject.Name, ILibrary.InstanceName, StringComparison.Ordinal);
    }

    private static void RequireCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new NeuronAuthorizationException("A grant command requires a command id.");
        }
    }
}
