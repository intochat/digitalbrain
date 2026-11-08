using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

public sealed class StateReaderSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadReturnsNullBeforeTheFirstSaveAndTheRecordAfterAReaction()
    {
        const string brain = "sr1";
        var alias = brain + ".vw";
        var deploy = await ((SynapseHost)silo.Services.GetRequiredService<ISynapseHost>()).Deploy(ShSource.Brain(brain), ArSource.Of(brain, "Board", VaSource.Board), ShSource.None, Ct);
        Assert.True(deploy.Status == DeployStatus.Deployed, $"{deploy.Status}: {deploy.Reason} {string.Join("; ", deploy.Diagnostics)}");
        var installs = silo.Get<IInstalls>($"{brain}/synapse.installs/default");
        await StateNeuronSiloFacts.Until(async () => await installs.Describe(alias, Ct) is not null, Ct);
        var reader = silo.Get<IStateReader>($"{brain}/{alias}/default");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await reader.Read(alias)).ValueKind);
        await silo.Services.GetRequiredService<IBrainJournalWriter>().Append(brain, null, $"{brain}/os.adapter/x",
            [new JournalSignalWrite(brain + ".polled", "{}"u8.ToArray())], Ct);

        await StateNeuronSiloFacts.Until(async () => (await reader.Read(alias)) is { ValueKind: System.Text.Json.JsonValueKind.Object } record
            && record.TryGetProperty("polls", out var polls) && polls.GetInt32() == 1, Ct);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await reader.Read("other.alias")).ValueKind);
        // The journal tail of the HTTP edge resolves the same brain history the runtime registered.
        Assert.NotNull(silo.Services.GetService<Func<BrainId, ISignalHistory>>());
    }
}
