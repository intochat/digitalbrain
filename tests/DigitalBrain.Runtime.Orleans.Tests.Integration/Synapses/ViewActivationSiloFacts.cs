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
        public sealed record Board(int Polls = 0, int Refreshes = 0, string? Value = null, string? TraceId = null, string? CausationId = null)
        {
            public Board() : this(0, 0, null, null, null) { }
        }

        public static class Unit
        {
            [Synapse("ar.polled", "ar.vw")]
            public static Board Seen(SignalEnvelope s, Board b) => b with { Polls = b.Polls + 1 };

            [Synapse("ar.refresh", "ar.vw")]
            public static Board Refreshed(SignalEnvelope s, Board b)
                => b with { Refreshes = b.Refreshes + 1, Value = s.Payload.TryGetProperty("value", out var v) ? v.GetString() : null, TraceId = s.TraceId, CausationId = s.CausationId };

            [Synapse("ar.vw")]
            public static View Show(Board b) => new("Board", new Row(
            [
                new Badge(b.Polls.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Button("refresh", "Refresh", new Emit("ar.vw", "ar.refresh")),
                new Button("foreign", "Foreign", new Emit("other.alias", "ar.refresh")),
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
        var root = view.View.GetProperty("root");
        Assert.Equal("row", root.GetProperty("kind").GetString());
        Assert.Equal("1", root.GetProperty("children")[0].GetProperty("label").GetString());
        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate(alias, view.Generation, "foreign", null));
        Assert.Equal(ActivationRefusedException.ForeignOwner, refused.Reason);

        await views.Activate(alias, view.Generation, "refresh", "now");
        await StateNeuronSiloFacts.Until(async () => (await State(brain))?.GetProperty("refreshes").GetInt32() == 1, Ct);

        Assert.Equal("now", (await State(brain))!.Value.GetProperty("value").GetString());
        var journaled = await silo.History(brain).Read(NeuronId.Parse($"{brain}/{alias}/default"), 0, 50, Ct);
        Assert.Contains(journaled.Signals, s => s.Signal is ViewChanged { Generation: > 0 });
        // A read leaves out the emit (no loaded type has its alias); the reaction's StateChanged, journaled next, names it as its cause.
        var changed = Assert.Single(journaled.Signals, s => s.Signal is StateChanged { Cause: var cause } && cause == brain + ".refresh");
        var state = (await State(brain))!.Value;
        Assert.Equal(Causation.Address(NeuronId.Parse($"{brain}/{alias}/default"), changed.Sequence - 1), state.GetProperty("causationId").GetString());
        Assert.NotNull(changed.TraceId);
        Assert.Equal(changed.TraceId, state.GetProperty("traceId").GetString());
    }
}
