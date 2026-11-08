using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// A deployed unit ("ar." becomes the brain) whose view counts polls and offers a refresh button it owns, and one owned by another alias.
internal static class VaSource
{
    internal const string Board = """
        using DigitalBrain.UI;

        namespace Me.ViewBoard;

        [State("ar.vw")]
        public sealed record Board(int Polls = 0, int Refreshes = 0, string? Value = null)
        {
            public Board() : this(0, 0, null) { }
        }

        public static class Unit
        {
            [Synapse("ar.polled", "ar.vw")]
            public static Board Seen(SignalEnvelope s, Board b) => b with { Polls = b.Polls + 1 };

            [Synapse("ar.refresh", "ar.vw")]
            public static Board Refreshed(SignalEnvelope s, Board b)
                => b with { Refreshes = b.Refreshes + 1, Value = s.Payload.TryGetProperty("value", out var v) ? v.GetString() : null };

            [Synapse("ar.vw")]
            public static View Show(Board b) => new("Board", new Row(
            [
                new Badge(b.Polls.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Button("Refresh", new ButtonAction("ar.vw", "ar.refresh", [])) { Key = "refresh", Emit = new Emit("ar.vw", "ar.refresh") },
                new Button("Foreign", new ButtonAction("other", "ar.refresh", [])) { Key = "foreign", Emit = new Emit("other.alias", "ar.refresh") },
            ]));
        }
        """;
}

public sealed class ViewActivationSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<System.Text.Json.JsonElement?> State(string brain)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, $"{brain}/{brain}.vw/default"));
        return await host.InspectState(Ct);
    }

    [Fact]
    public async Task ActivatePublishesTheEmitToTheOwnerOnTheSilo()
    {
        const string brain = "va1";
        var alias = brain + ".vw";
        var deploy = await ((SynapseHost)silo.Services.GetRequiredService<ISynapseHost>()).Deploy(ShSource.Brain(brain), ArSource.Of(brain, "Board", VaSource.Board), ShSource.None, Ct);
        Assert.True(deploy.Status == DeployStatus.Deployed, $"{deploy.Status}: {deploy.Reason} {string.Join("; ", deploy.Diagnostics)}");
        var installs = silo.Get<IInstalls>($"{brain}/synapse.installs/default");
        await StateNeuronSiloFacts.Until(async () => await installs.Describe(alias, Ct) is not null, Ct);
        await silo.Services.GetRequiredService<IBrainJournalWriter>().Append(brain, null, $"{brain}/os.adapter/x",
            [new JournalSignalWrite(brain + ".polled", "{}"u8.ToArray())], Ct);
        var views = silo.Get<IViewReader>($"{brain}/{alias}/default");
        await StateNeuronSiloFacts.Until(async () => await views.Read(alias) is not null, Ct);

        var view = (await views.Read(alias))!;
        var row = Assert.IsType<DigitalBrain.UI.Row>(view.View.Root);
        Assert.Equal("1", Assert.IsType<DigitalBrain.UI.Badge>(row.Children[0]).Label);
        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate(alias, view.Generation, "foreign", null));
        Assert.Equal(ActivationRefusedException.ForeignOwner, refused.Reason);

        await views.Activate(alias, view.Generation, "refresh", "now");
        await StateNeuronSiloFacts.Until(async () => (await State(brain))?.GetProperty("refreshes").GetInt32() == 1, Ct);

        Assert.Equal("now", (await State(brain))!.Value.GetProperty("value").GetString());
        var journaled = await silo.History(brain).Read(NeuronId.Parse($"{brain}/{alias}/default"), 0, 50, Ct);
        Assert.Contains(journaled.Signals, s => s.Signal is ViewChanged { Generation: > 0 });
    }
}
