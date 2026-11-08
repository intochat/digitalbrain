using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

// Two deployed units: B reacts to "{brain}.polled" by alias only and is installed first, while no unit has registered the alias;
// A (deployed after) declares Polled and a reaction that echoes Poke as Polled, so a chained Polled carries A's cause.
internal static class ArSource
{
    internal const string Reactor = """
        namespace Me.AliasDash;

        [State("ar.dash")]
        public sealed record Dash(int Count = 0, string? Alias = null, string? TraceId = null, string? CausationId = null)
        {
            public Dash() : this(0, null, null, null) { }
        }

        public static class Board
        {
            [Synapse("ar.polled", "ar.dash")]
            public static Dash Seen(SignalEnvelope s, Dash d) => new(s.Payload.GetProperty("count").GetInt32(), s.Alias, s.TraceId, s.CausationId);
        }
        """;

    internal const string Publisher = """
        namespace Me.AliasPoller;

        [Signal("ar.polled")]
        public sealed record Polled(int Count) : Signal;

        [Signal("ar.poke")]
        public sealed record Poke(int Count) : Signal;

        [State("ar.poller")]
        public sealed record Poller(int Echoes = 0)
        {
            public Poller() : this(0) { }
        }

        public static class Echo
        {
            [Synapse(typeof(Poke), "ar.poller")]
            public static Reaction<Poller> Back(Poke p, Poller s) => Reaction<Poller>.Of(s with { Echoes = s.Echoes + 1 }, new Polled(p.Count));
        }
        """;

    internal static SynapseSource Of(string brain, string key, string text) =>
        new(key + brain, new Dictionary<string, string> { [key + ".cs"] = text.Replace("\"ar.", $"\"{brain}.", StringComparison.Ordinal) }, null);
}

public interface IArPublisher
{
    Task Publish(string alias, int count);
}

// Publishes a signal of a deployed unit by its alias: the type is looked up in the silo's signal map on each call.
[Neuron("ar.publisher")]
public sealed class ArPublisher : Neuron<SbPayState>, IArPublisher
{
    public async Task Publish(string alias, int count)
    {
        var map = ((JournalPayloads)Context.Services.GetRequiredService(typeof(JournalPayloads))).Types;
        if (!map.TryResolve(alias, out var type)) { throw new InvalidOperationException($"'{alias}' is not loaded"); }
        await Save(State with { Count = State.Count + 1 }, (Signal)Activator.CreateInstance(type, count)!);
    }
}

public sealed class AliasReactionSiloFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SynapseHost Host => (SynapseHost)silo.Services.GetRequiredService<ISynapseHost>();

    private async Task Deploy(string brain, string key, string text, string alias)
    {
        var outcome = await Host.Deploy(ShSource.Brain(brain), ArSource.Of(brain, key, text), ShSource.None, Ct);
        Assert.True(outcome.Status == DeployStatus.Deployed, $"{outcome.Status}: {outcome.Reason} {string.Join("; ", outcome.Diagnostics)}");
        var installs = silo.Get<IInstalls>($"{brain}/synapse.installs/default");
        await StateNeuronSiloFacts.Until(async () => await installs.Describe(alias, Ct) is not null, Ct);
    }

    private async Task<System.Text.Json.JsonElement?> Board(string brain)
    {
        var host = silo.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create(NeuronTypeRegistry.AffineGrainType, $"{brain}/{brain}.dash/default"));
        return await host.InspectState(Ct);
    }

    [Fact]
    public async Task AReactionByAliasReceivesTheEnvelopeOnTheSilo()
    {
        const string brain = "ar1";
        var polled = brain + ".polled";
        Assert.False(silo.Services.GetRequiredService<JournalPayloads>().Types.TryResolve(polled, out _));
        await Deploy(brain, "Dash", ArSource.Reactor, brain + ".dash");
        await Deploy(brain, "Poller", ArSource.Publisher, brain + ".poller");
        var publisher = NeuronId.Parse($"{brain}/ar.publisher/p");
        var pub = silo.Get<IArPublisher>(publisher.ToString());

        // A root publish: the journal stamps a trace and no causation.
        await pub.Publish(polled, 3);
        await StateNeuronSiloFacts.Until(async () => (await Board(brain))?.GetProperty("count").GetInt32() == 3, Ct);
        var root = (await silo.History(brain).Read(publisher, 0, 50, Ct)).Signals.Single(s => s.Signal.GetType().Name == "Polled");
        var board = (await Board(brain))!.Value;
        Assert.Equal(polled, board.GetProperty("alias").GetString());
        Assert.NotNull(root.TraceId);
        Assert.Equal(root.TraceId, board.GetProperty("traceId").GetString());
        Assert.Null(root.CausationId);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, board.GetProperty("causationId").ValueKind);

        // A chained publish: A's reaction to Poke publishes Polled, whose envelope names the Poke entry as its cause.
        await pub.Publish(brain + ".poke", 5);
        await StateNeuronSiloFacts.Until(async () => (await Board(brain))?.GetProperty("count").GetInt32() == 5, Ct);
        board = (await Board(brain))!.Value;
        var poller = NeuronId.Parse($"{brain}/{brain}.poller/default");
        var echoed = (await silo.History(brain).Read(poller, 0, 50, Ct)).Signals.Single(s => s.Signal.GetType().Name == "Polled");
        // The cause address names the Poke entry by its brain journal position.
        var poke = (await silo.History(brain).Read(0, 100, Ct)).Signals.Single(s => s.Signal.GetType().Name == "Poke");
        Assert.Equal(Causation.Address(publisher, poke.Sequence), echoed.CausationId);
        Assert.Equal(echoed.CausationId, board.GetProperty("causationId").GetString());
        Assert.Equal(echoed.TraceId, board.GetProperty("traceId").GetString());
    }

    // No loaded unit provides the alias's type (an adapter journals it): the envelope carries the journal's raw JSON.
    [Fact]
    public async Task AnAliasWithNoLoadedTypeDeliversTheRawPayload()
    {
        const string brain = "ar2";
        await Deploy(brain, "Dash", ArSource.Reactor, brain + ".dash");
        var writer = silo.Services.GetRequiredService<IBrainJournalWriter>();

        await writer.Append(brain, null, $"{brain}/os.adapter/x", [new JournalSignalWrite(brain + ".polled", System.Text.Encoding.UTF8.GetBytes("""{"count":7}"""))], Ct);
        await StateNeuronSiloFacts.Until(async () => (await Board(brain))?.GetProperty("count").GetInt32() == 7, Ct);

        Assert.Equal(brain + ".polled", (await Board(brain))!.Value.GetProperty("alias").GetString());
    }
}
