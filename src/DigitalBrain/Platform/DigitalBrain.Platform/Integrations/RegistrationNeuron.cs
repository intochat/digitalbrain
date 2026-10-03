using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Platform.Secrets;
using Orleans.Runtime;

namespace DigitalBrain.Platform.Integrations;

[GrainType("integration.registration")]
internal sealed class RegistrationNeuron : Neuron<RegistrationState>, IIntegrationRegistration
{
    private const int MaxValueLength = 16 * 1024;

    private readonly IReadOnlyList<IntegrationDefinition> _definitions;
    private readonly IGrainFactory _grains;
    private readonly RegistrationCredentials _credentials;
    private IntegrationDefinition? _definition;

    public RegistrationNeuron(
        [PersistentState("registration", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<RegistrationState> store,
        IReadOnlyList<IntegrationDefinition> definitions,
        IGrainFactory grains, RegistrationCredentials credentials) : base(store)
    {
        _definitions = definitions;
        _grains = grains;
        _credentials = credentials;
    }

    private IntegrationDefinition Definition => _definition ??= Resolve();

    public Task<RegistrationSnapshot> Read() => Task.FromResult(SnapshotOf(Snapshot));

    public async Task<RegistrationSnapshot> Configure(ConfigureRegistration request)
    {
        RefuseAppCalls();
        return await Apply(request);
    }

    public async Task<RegistrationSnapshot> SeedIfUnconfigured(ConfigureRegistration request)
    {
        RefuseAppCalls();
        // A cleared registration was an operator's decision; seeds only ever fill virgin state.
        return SnapshotOf(Snapshot).Status == RegistrationStatus.Unconfigured && Snapshot.Revision == 0
            ? await Apply(request)
            : SnapshotOf(Snapshot);
    }

    private async Task<RegistrationSnapshot> Apply(ConfigureRegistration request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var values = request.Values;
        if (values is null || values.Count == 0)
        {
            throw new ArgumentException("Provide at least one value.");
        }

        // Rejected before anything is stored, and without echoing what was submitted.
        if (values.Keys.Any(field => !Definition.AllFields.Contains(field, StringComparer.Ordinal)))
        {
            throw new ArgumentException($"A submitted field is not part of this integration; it accepts: {string.Join(", ", Definition.AllFields)}.");
        }

        if (values.Values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength))
        {
            throw new ArgumentException($"Every value must be non-empty and at most {MaxValueLength} characters.");
        }

        var next = CopyOf(Snapshot);
        foreach (var (field, value) in values)
        {
            next.References[field] = await _credentials.Store(Definition, field, value);
            if (Definition.IsSetting(field))
            {
                next.Settings[field] = value;
            }
        }

        next.Revision++;
        var snapshot = SnapshotOf(next);
        await Save(next, new RegistrationChanged(Definition.Id, snapshot.Status));
        return snapshot;
    }

    public async Task<RegistrationSnapshot> Clear(string field)
    {
        RefuseAppCalls();
        if (field is null || !Definition.AllFields.Contains(field, StringComparer.Ordinal))
        {
            throw new ArgumentException($"The field is not part of this integration; it accepts: {string.Join(", ", Definition.AllFields)}.");
        }

        if (!Snapshot.References.ContainsKey(field))
        {
            return SnapshotOf(Snapshot);
        }

        var next = CopyOf(Snapshot);
        next.References.Remove(field);
        next.Settings.Remove(field);
        await _credentials.Remove(Definition, field);
        next.Revision++;
        var snapshot = SnapshotOf(next);
        await Save(next, new RegistrationChanged(Definition.Id, snapshot.Status));
        return snapshot;
    }

    public async Task<ReleasedRegistration> Release(CallerContext caller)
    {
        // Release is unreachable from scripts ([PlatformOnly]); an app-stamped ambient context is a platform call chain, not an app caller.
        // Values leave only to trusted platform code: a module's own grain call stamped by the Platform
        // edge. Users and assistants (authenticated HTTP), installed apps (app proxy) and the scheduler
        // never qualify, so no HTTP route or script can obtain a provider credential.
        if (caller is null || !CallerContextStamper.IsTrusted(caller)
            || caller.Kind != CallerKind.Platform || caller.StampedBy != TrustedEdge.Platform)
        {
            throw new InvalidOperationException("Only trusted platform code can release a registration.");
        }

        if (SnapshotOf(Snapshot).Status != RegistrationStatus.Ready)
        {
            throw new InvalidOperationException("The registration is not fully configured.");
        }

        var vault = _grains.GetGrain<ISecrets>(IntegrationVault.Owner);
        var releaser = caller with { AppId = IntegrationVault.CallerAppId };
        var values = new Dictionary<string, string>(Snapshot.References.Count, StringComparer.Ordinal);
        foreach (var (field, reference) in Snapshot.References)
        {
            values[field] = await vault.Resolve(releaser, reference);
        }

        return new ReleasedRegistration { Values = values };
    }

    // Scripts run under an App stamp; nothing an installed app does may touch a provider registration,
    // whatever parameters it passes. Platform HTTP paths carry User and startup work carries no stamp.
    private static void RefuseAppCalls()
    {
        if (CallerContextStamper.TryGet(out var ambient) && (ambient.Kind == CallerKind.App || ambient.StampedBy == TrustedEdge.AppProxy))
        {
            throw new InvalidOperationException("Installed apps cannot use provider registrations.");
        }
    }

    private static RegistrationState CopyOf(RegistrationState state) => new()
    {
        References = new(state.References, StringComparer.Ordinal),
        Settings = new(state.Settings, StringComparer.Ordinal),
        Revision = state.Revision,
    };

    private RegistrationSnapshot SnapshotOf(RegistrationState state)
    {
        var missing = Definition.RequiredFields.Where(field => !state.References.ContainsKey(field)).ToArray();
        var status = missing.Length == 0 ? RegistrationStatus.Ready
            : state.References.Count == 0 ? RegistrationStatus.Unconfigured
            : RegistrationStatus.Partial;
        return new RegistrationSnapshot { IntegrationId = Definition.Id, Status = status, MissingFields = missing, Settings = new(state.Settings, StringComparer.Ordinal), Revision = state.Revision };
    }

    private IntegrationDefinition Resolve()
    {
        var key = this.GetPrimaryKeyString();
        if (key.StartsWith(IntegrationVault.GrainKeyPrefix, StringComparison.Ordinal))
        {
            var id = key[IntegrationVault.GrainKeyPrefix.Length..];
            var found = _definitions.FirstOrDefault(definition => definition.Id == id);
            if (found is not null)
            {
                return found;
            }
        }

        throw new InvalidOperationException("No composed module declares this integration.");
    }

}
