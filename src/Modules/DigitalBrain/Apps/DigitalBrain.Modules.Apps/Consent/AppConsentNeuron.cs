using DigitalBrain.Apps.Signals;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Apps.Consent;

[GenerateSerializer, Alias("apps.consent-state")]
internal sealed record AppConsentState
{
    [Id(0)] public Dictionary<string, DateTimeOffset> Approved { get; init; } = new(StringComparer.Ordinal);
}

// Keyed by the workspace scope id. Review returns the sheet; Approve records the consent and then
// installs the app into the workspace catalog, so the install proceeds in the same step.
[GrainType("app-consent")]
internal sealed class AppConsentNeuron(
    [PersistentState("app-consent", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppConsentState> store)
    : Neuron<AppConsentState>(store), IAppConsent
{
    public async Task<AppConsentSheet> Review(string appId)
    {
        var manifest = await Manifest(appId);
        return ConsentSheetBuilder.Build(manifest, Snapshot.Approved.ContainsKey(appId));
    }

    public async Task<AppConsentSheet> Approve(string appId)
    {
        var manifest = await Manifest(appId);
        if (!Snapshot.Approved.ContainsKey(appId))
        {
            var next = new AppConsentState
            {
                Approved = new Dictionary<string, DateTimeOffset>(Snapshot.Approved, StringComparer.Ordinal)
                {
                    [appId] = DateTimeOffset.UtcNow,
                },
            };
            await Save(next, new AppConsentApproved(appId, manifest.Version, DateTimeOffset.UtcNow));
        }

        await GrainFactory.GetGrain<IAppCatalog>(this.GetPrimaryKeyString()).Install(manifest);
        return ConsentSheetBuilder.Build(manifest, true);
    }

    public Task<bool> IsApproved(string appId) => Task.FromResult(Snapshot.Approved.ContainsKey(appId));

    private async Task<AppManifest> Manifest(string appId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        var catalogued = await GrainFactory.GetGrain<IAppManifestDirectory>(AppManifestDirectoryGrains.Key).Read();
        var entry = catalogued.FirstOrDefault(scoped => string.Equals(scoped.Manifest.Id, appId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No catalogued app '{appId}' is available for consent.");
        return entry.Manifest;
    }
}
