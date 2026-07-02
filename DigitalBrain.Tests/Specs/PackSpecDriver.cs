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
