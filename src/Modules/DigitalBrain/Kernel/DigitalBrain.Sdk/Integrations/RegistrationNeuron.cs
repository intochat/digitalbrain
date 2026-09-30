using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Sdk.Secrets;
using Orleans.Runtime;

namespace DigitalBrain.Sdk.Integrations;

[GrainType("integration.registration")]
internal sealed class RegistrationNeuron : Neuron<RegistrationState>, IIntegrationRegistration
{
    private const int MaxValueLength = 16 * 1024;

    private readonly IReadOnlyList<IntegrationDefinition> _definitions;
    private readonly IGrainFactory _grains;
    private IntegrationDefinition? _definition;

    public RegistrationNeuron(
        [PersistentState("registration", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<RegistrationState> store,
        IReadOnlyList<IntegrationDefinition> definitions,
        IGrainFactory grains) : base(store)
    {
        _definitions = definitions;
        _grains = grains;
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
        return SnapshotOf(Snapshot).Status == RegistrationStatus.Unconfigured
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

        var vault = _grains.GetGrain<ISecrets>(IntegrationVault.Owner);
        var next = CopyOf(Snapshot);
        foreach (var (field, value) in values)
        {
            next.References[field] = await vault.Set(PlatformCaller(), IntegrationVault.SecretName(Definition.Id, field), $"{Definition.Id} {field}", value);
            if (Definition.SettingFields.Contains(field, StringComparer.Ordinal))
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
        await _grains.GetGrain<ISecrets>(IntegrationVault.Owner).Remove(PlatformCaller(), IntegrationVault.SecretName(Definition.Id, field));
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
        var missing = Definition.AllFields.Where(field => !state.References.ContainsKey(field)).ToArray();
        var status = missing.Length == 0 ? RegistrationStatus.Ready
            : missing.Length == Definition.AllFields.Count ? RegistrationStatus.Unconfigured
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

    private static CallerContext PlatformCaller() => new()
    {
        PrincipalId = IntegrationVault.CallerAppId,
        AccountId = IntegrationVault.Owner,
        BrainId = IntegrationVault.Owner,
        Kind = CallerKind.Platform,
        StampedBy = TrustedEdge.Platform,
        AppId = IntegrationVault.CallerAppId,
    };
}
