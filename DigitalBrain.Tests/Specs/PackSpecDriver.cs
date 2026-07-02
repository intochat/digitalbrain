using DigitalBrain.Core;
using DigitalBrain.Kernel;
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

    public Task BroadcastAsync<T>(T synapse) where T : Synapse =>
        host.Grain<IGeneratedNeuron>(GeneratedKeyFor("DriverProbePack")).FireAsync(synapse).AsTask();

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
}
