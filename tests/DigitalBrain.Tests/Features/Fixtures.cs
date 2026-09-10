using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Tests;
using Orleans.Concurrency;
using Orleans.Runtime;

[assembly: NeuronJsonContext(typeof(CounterJson))]

namespace DigitalBrain.Tests;

[GenerateSerializer]
[Alias("db.test.profile-state")]
public sealed record ProfileState([property: Id(0)] string Bio);

[Alias("db.test.profile")]
public interface IProfile : IGrainWithStringKey
{
    [Alias(nameof(ReadBio))]
    Task<string?> ReadBio();
}

// A Neuron<TState>: reacts to "SetBio" {"bio":"..."} by saving a snapshot. Everything else is ignored.
[GrainType("profile")]
internal sealed class Profile(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<ProfileState>> state)
    : Neuron<ProfileState>(runtime, state), IProfile
{
    public Task<string?> ReadBio() => Task.FromResult(State?.Bio);

    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "SetBio")
        {
            return Task.CompletedTask;
        }

        using var body = JsonDocument.Parse(delivery.Signal.Body);
        return SaveAsync(new ProfileState(body.RootElement.GetProperty("bio").GetString() ?? ""), cancellationToken);
    }
}

// Replies Pong{n} to the source of every Ping{n}, from inside ReceiveAsync.
[GrainType("echo")]
internal sealed class EchoNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type == "Ping"
            ? FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}

// Test switches shared across activations of fixture grains in one silo.
public static class FixtureSwitches
{
    public static ConcurrentDictionary<string, int> FlakyFailuresLeft { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, byte> HeldQueues { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> DeliveryFailuresLeft { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> Reactions { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> ReactionFailuresLeft { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, TaskCompletionSource> Cancelled { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, TaskCompletionSource> Release { get; } = new(StringComparer.Ordinal);
}

internal sealed class CounterFixtureState
{
    public ConcurrentDictionary<string, byte> FailingReactions { get; } = new(StringComparer.Ordinal);

    public ConcurrentDictionary<CommandId, string> LostTurns { get; } = new();

    public ConcurrentDictionary<string, int> Executions { get; } = new(StringComparer.Ordinal);
}

// Throws on the first reaction to each entry while FlakyFailuresLeft[name] > 0, then echoes.
[GrainType("flaky")]
internal sealed class FlakyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var key = $"{Id.Name}:{delivery.Sequence}";
        FixtureSwitches.Reactions.AddOrUpdate(key, 1, (_, n) => n + 1);
        if (FixtureSwitches.FlakyFailuresLeft.AddOrUpdate(Id.Name, 0, (_, left) => left - 1) >= 0)
        {
            throw new InvalidOperationException("flaky: first reaction fails");
        }

        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}

[GrainType("slow")]
internal sealed class SlowNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await FixtureSwitches.Release[Id.Name].Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            FixtureSwitches.Cancelled[Id.Name].TrySetResult();
            throw;
        }

        // Pong lets tests observe the reaction from the firing neuron without touching the slow neuron.
        await FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}

[GrainType("failing")]
internal sealed class FailingNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        while (FixtureSwitches.ReactionFailuresLeft.TryGetValue(Id.Name, out var failuresLeft) && failuresLeft > 0)
        {
            if (FixtureSwitches.ReactionFailuresLeft.TryUpdate(Id.Name, failuresLeft - 1, failuresLeft))
            {
                throw new InvalidOperationException("failing: reaction fails");
            }
        }

        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}

[GrainType("scheduling")]
internal sealed class SchedulingNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type == "Start")
        {
            Schedule(Signal.Create("Work", JsonSerializer.Serialize(new { source = delivery.Source.ToString() })));
            // A reaction awaiting anything lets another write flush what it staged; this stands in for that race.
            await PersistAsync();
            if (FixtureSwitches.ReactionFailuresLeft.TryRemove(Id.Name, out var failuresLeft) && failuresLeft > 0)
            {
                throw new InvalidOperationException("scheduling: first reaction fails after scheduling");
            }
        }
        else if (delivery.Signal.Type == "Work")
        {
            using var body = JsonDocument.Parse(delivery.Signal.Body);
            if (!NeuronId.TryParse(body.RootElement.GetProperty("source").GetString(), out var source))
            {
                throw new InvalidOperationException("scheduling: work is missing its original source");
            }

            await FireAsync(Signal.Create("Pong", "{}"), source, delivery.CorrelationId, cancellationToken);
        }
    }
}

[GenerateSerializer]
[Alias("db.test.counter-state")]
public sealed record CounterState([property: Id(0)] int Total);

[GenerateSerializer]
[Alias("db.test.add-count")]
public sealed record AddCount(
    CommandId Id,
    [property: Id(0)] int Count,
    [property: Id(1)] string Note = "") : Command(Id);

[Alias("test.counter")]
public interface ICounter : INeuron
{
    /// <summary>Adds a count and schedules the increment.</summary>
    [Alias("add")]
    Task<Accepted<int>> Add(AddCount command);

    [Alias("save")]
    Task<Accepted<int>> AddAndSave(AddCount command);

    [Alias("callout")]
    Task<Accepted<int>> AddAndCallOut(AddCount command);

    [ReadOnly]
    [Alias("total")]
    Task<int> ReadTotal();
}

[GenerateSerializer]
[Alias("db.test.bad-arguments")]
public sealed record BadArguments(
    CommandId Id,
    [property: Id(0)] Dictionary<string, string> Tags) : Command(Id);

// This interface exists to prove the descriptor rules reject it.
[Alias("test.bad")]
public interface IBadCounter : INeuron
{
    [Alias("bad")]
    Task<int> Bad(BadArguments command);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BadArguments))]
[JsonSerializable(typeof(AddCount))]
[JsonSerializable(typeof(Accepted<int>))]
[JsonSerializable(typeof(int))]
internal sealed partial class CounterJson : JsonSerializerContext;

[GrainType("counter")]
internal sealed class CounterNeuron(
    NeuronRuntime runtime,
    CounterFixtureState fixtureState,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<CounterState>> state)
    : Neuron<CounterState>(runtime, state), ICounter
{
    public Task<Accepted<int>> Add(AddCount command) => ExecuteCommandAsync(
        Descriptor("add"), command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            fixtureState.Executions.AddOrUpdate(Id.Name, 1, (_, count) => count + 1);
            var work = Schedule(Signal.Create("Counted", "{\"amount\":" + arguments.Count + "}"));
            return new Accepted<int>(arguments.Count, work);
        });

    public Task<Accepted<int>> AddAndSave(AddCount command) => ExecuteCommandAsync(
        Descriptor("save"), command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            // The fixture deliberately breaks the reaction-only snapshot rule.
            _ = SaveAsync(new CounterState(arguments.Count));
            return new Accepted<int>(arguments.Count, default);
        });

    public Task<Accepted<int>> AddAndCallOut(AddCount command) => ExecuteCommandAsync(
        Descriptor("callout"), command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            // The fixture deliberately breaks the commands-are-local rule; the outgoing filter rejects before dispatch, so the wait never blocks.
            GrainFactory.GetGrain<INeuron>(NeuronId.Plain("bystander").ToGrainId()).ReadState().GetAwaiter().GetResult();
            return new Accepted<int>(arguments.Count, default);
        });

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (fixtureState.FailingReactions.ContainsKey(Id.Name))
        {
            throw new InvalidOperationException("counter: every reaction fails");
        }

        if (delivery.Signal.Type != "Counted")
        {
            return;
        }

        using var body = JsonDocument.Parse(delivery.Signal.Body);
        var amount = body.RootElement.GetProperty("amount").GetInt32();
        await SaveAsync(new CounterState((State?.Total ?? 0) + amount), cancellationToken);
        await FireAsync(Signal.Create("Counted", delivery.Signal.Body), null, delivery.CorrelationId, cancellationToken);
    }

    public Task<int> ReadTotal() => Task.FromResult(State?.Total ?? 0);
}

// The kernel calls this before staging a command's terminal record. The test
// implementation simulates process loss for one command id so the silo restart finds only Attempted.
internal sealed class FixtureCommandCrashPoint(CounterFixtureState fixtureState) : ICommandCrashPoint
{
    internal static ConcurrentDictionary<CommandId, byte> CrashOnce { get; } = new();

    public void BeforeTerminalRecord(CommandId command)
    {
        if (fixtureState.LostTurns.TryRemove(command, out var name))
        {
            // Simulates the wrapper losing its turn after Attempted without the fence resolving it.
            throw new NeuronPersistenceException(new NeuronId("counter", name), "simulated lost command turn", new IOException("turn lost"));
        }

        if (CrashOnce.TryRemove(command, out _))
        {
            throw new InvalidOperationException(
                "simulated process loss before the terminal command record was committed");
        }
    }
}
