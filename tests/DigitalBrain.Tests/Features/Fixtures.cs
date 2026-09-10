using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

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
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ProfileState> state)
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

    public static ConcurrentDictionary<string, int> Reactions { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, bool> Asleep { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> ThrowingFailuresLeft { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, TaskCompletionSource> Cancelled { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, TaskCompletionSource> Release { get; } = new(StringComparer.Ordinal);

    public static ConcurrentDictionary<string, int> CommandExecutions { get; } = new(StringComparer.Ordinal);
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

// While Asleep[name] is true the reaction throws (so the cursor stays); when awake it echoes.
[GrainType("sleepy")]
internal sealed class SleepyNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (FixtureSwitches.Asleep.TryGetValue(Id.Name, out var asleep) && asleep)
        {
            throw new InvalidOperationException("sleepy: not yet");
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

[GrainType("throwing")]
internal sealed class ThrowingNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (FixtureSwitches.ThrowingFailuresLeft[Id.Name] > 0)
        {
            FixtureSwitches.ThrowingFailuresLeft[Id.Name]--;
            throw new InvalidOperationException("throwing: reaction fails");
        }

        return FireAsync(Signal.Create("Pong", delivery.Signal.Body), delivery.Source, delivery.CorrelationId, cancellationToken);
    }
}

[GenerateSerializer]
[Alias("db.test.counter-state")]
public sealed record CounterState([property: Id(0)] int Total);

[GenerateSerializer]
[Alias("db.test.add-count")]
public sealed record AddCount(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] int Amount,
    [property: Id(2)] string Note = "") : Command(CommandId);

[Alias("test.counter")]
public interface ICounter : INeuron
{
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

[JsonSerializable(typeof(AddCount))]
[JsonSerializable(typeof(Accepted<int>))]
internal sealed partial class CounterJson : JsonSerializerContext;

[GrainType("counter")]
internal sealed class CounterNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CounterState> state)
    : Neuron<CounterState>(runtime, state), ICounter
{
    private static readonly CommandDescriptor AddCommand = new("test.counter", "add");
    private static readonly CommandDescriptor SaveCommand = new("test.counter", "save");
    private static readonly CommandDescriptor CallOutCommand = new("test.counter", "callout");

    public Task<Accepted<int>> Add(AddCount command) => ExecuteCommandAsync(
        AddCommand, command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            FixtureSwitches.CommandExecutions.AddOrUpdate(Id.Name, 1, (_, count) => count + 1);
            var work = Schedule(Signal.Create("Counted", "{\"amount\":" + arguments.Amount + "}"));
            return new Accepted<int>(arguments.Amount, work);
        });

    public Task<Accepted<int>> AddAndSave(AddCount command) => ExecuteCommandAsync(
        SaveCommand, command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            // The fixture deliberately breaks the reaction-only snapshot rule.
            _ = SaveAsync(new CounterState(arguments.Amount));
            return new Accepted<int>(arguments.Amount, default);
        });

    public Task<Accepted<int>> AddAndCallOut(AddCount command) => ExecuteCommandAsync(
        CallOutCommand, command, CounterJson.Default.AddCount, CounterJson.Default.AcceptedInt32, arguments =>
        {
            // The fixture deliberately breaks the commands-are-local rule; the outgoing filter rejects before dispatch, so the wait never blocks.
            GrainFactory.GetGrain<INeuron>(NeuronId.Plain("bystander").ToGrainId()).ReadState().GetAwaiter().GetResult();
            return new Accepted<int>(arguments.Amount, default);
        });

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
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
internal sealed class FixtureCommandCrashPoint : ICommandCrashPoint
{
    internal static ConcurrentDictionary<CommandId, byte> CrashOnce { get; } = new();

    public void BeforeTerminalRecord(CommandId command)
    {
        if (CrashOnce.TryRemove(command, out _))
        {
            throw new InvalidOperationException(
                "simulated process loss before the terminal command record was committed");
        }
    }
}
