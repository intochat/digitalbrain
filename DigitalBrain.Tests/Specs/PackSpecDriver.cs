using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Foundry;
using Xunit;

namespace DigitalBrain.Tests.Specs;

public interface INeuronTestHost
{
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
    Task FireAsync<T>(T synapse) where T : Synapse;
}

public sealed class PackSpecDriver(INeuronTestHost host)
{
    private static string GeneratedKeyFor(string packName) => "generated-" + packName.ToLowerInvariant();

    public Task PublishPackAsync(string name, string version, string code, string ownerId = "spec-author") =>
        host.Grain<IMarketplaceNeuron>("market-spec").FireAsync(
            new PublishToMarketplace(name, version, Code: code, OwnerId: ownerId, IsPrivate: false, CommissionRate: 0.0)).AsTask();

    public Task InstallPackAsync(string name, string version, string buyerId = "spec-buyer") =>
        host.Grain<IMarketplaceNeuron>("market-spec").FireAsync(
            new InstallFromMarketplace(name, version, BuyerId: buyerId)).AsTask();

    public Task FireSynapseAtPackAsync(string packName, Synapse synapse) =>
        host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).FireAsync(synapse).AsTask();

    // Finds (and activates) an IDemoNeuron whose activation lands on a different silo than the named
    // pack's already-activated grain, so a subsequent broadcast from it is real proof of cross-silo
    // delivery rather than a same-grain loopback. Bounded trial rather than forcing placement: Orleans'
    // default placement strategy distributes distinctly-keyed grains across silos, so with 3 silos in
    // the cluster the odds of exhausting every attempt on the pack's own silo are negligible. Any
    // rejected same-silo candidates stay activated for the rest of the scenario (harmless here — nothing
    // asserts on total activation/subscriber count), but a future scenario relying on that count should
    // route through a helper that deactivates them instead of assuming this method leaves none behind.
    public async Task<string> ActivateBroadcasterOnDifferentSiloAsync(string packName)
    {
        var packSilo = await host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).GetSiloIdentityAsync();

        const int maxAttempts = 12;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var key = $"cross-silo-broadcaster-{attempt}";
            var broadcasterSilo = await host.Grain<IDemoNeuron>(key).GetSiloIdentityAsync();
            if (broadcasterSilo != packSilo)
                return key;
        }

        throw new InvalidOperationException(
            $"Could not activate a demo neuron on a different silo than pack '{packName}' (silo '{packSilo}') after {maxAttempts} attempts.");
    }

    public Task BroadcastFromAsync<T>(string broadcasterKey, T synapse) where T : Synapse =>
        host.Grain<IDemoNeuron>(broadcasterKey).FireAsync(synapse).AsTask();

    public async Task AssertBroadcastObservedAsync(string packName)
    {
        var receiver = host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName));

        // Orleans stream delivery is asynchronous (a pulling agent fans broadcasts out to subscribers
        // off the publisher's turn), so the receiver's incoming journal may not reflect the broadcast
        // the instant FireAsync returns. Poll rather than assert immediately, mirroring the retry
        // pattern every other broadcast-reactivity test in this suite already uses (e.g.
        // BroadcastReactivityTests.WaitForCountAsync, PackBroadcastReactivityTests.WaitForEmissionDeltaAsync).
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var incoming = await receiver.GetIncomingTimelineAsync();
            if (incoming.Any(s => s is DemoMessageSynapse d && d.Text == "cross-silo-probe"))
                return;
            await Task.Delay(50);
        }

        var final = await receiver.GetIncomingTimelineAsync();
        Assert.Contains(final, s => s is DemoMessageSynapse d && d.Text == "cross-silo-probe");
    }

    public async Task<IReadOnlyList<PackEmission>> GetEmissionsAsync(string packName)
    {
        var timeline = await host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).GetTimelineAsync();
        return timeline.OfType<PackEmission>().ToList();
    }

    public async Task AssertEmittedAsync(string packName, string expectedOutput)
    {
        var emissions = await GetEmissionsAsync(packName);
        Assert.Contains(emissions, e => e.Pack == packName && e.Output == expectedOutput);
    }

    // Same compile call PackAlcEmbodier.Embody uses for real published packs (assembly name + the
    // IPackBehavior-carrying assembly as an extra reference) — this exercises the production compile
    // path, not a reimplementation of it, so a CapabilityGate regression here means real embodiment breaks too.
    public IReadOnlyList<string> CheckCompilation(string code)
    {
        var compilation = FoundryCompilation.CreateWith("spec_" + Guid.NewGuid().ToString("N"), code, typeof(IPackBehavior).Assembly);
        return CapabilityGate.FindViolations(compilation);
    }
}
