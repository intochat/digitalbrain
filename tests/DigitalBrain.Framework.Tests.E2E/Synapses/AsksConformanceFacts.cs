using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Modules;
using DigitalBrain.Neurons;
using DigitalBrain.Testing;
using DigitalBrain.Units;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Framework.Tests.E2E.Synapses;

[Capability("asks-conformance-probe", 1)]
[System.ComponentModel.Description("Conformance probe for durable Asks execution")]
public interface IAsksConformanceProbe
{
    Task<JsonElement> Execute(string mode, CancellationToken ct = default);
}

[Signal("e2e.asks.kick")]
public sealed record AsksConformanceKick(string Key, string Mode, bool Repeatable = false) : Signal;

[State("e2e-asks")]
public sealed record AsksConformanceState(
    int Completed = 0,
    string? LastKey = null,
    string? LastResult = null,
    string? LastError = null)
{
    public AsksConformanceState() : this(0, null, null, null) { }
}

public interface IAsksConformanceDriver
{
    Task Kick(string key, string mode, bool repeatable = false);
}

[Neuron("e2e-asks-driver")]
public sealed class AsksConformanceDriver : Neuron<AsksConformanceDriver.DriverState>, IAsksConformanceDriver
{
    public Task Kick(string key, string mode, bool repeatable = false) =>
        Save(State with { Sent = State.Sent + 1 }, new AsksConformanceKick(key, mode, repeatable));

    public sealed record DriverState
    {
        public int Sent { get; init; }
    }
}

public static class AsksConformanceUnit
{
    [Synapse(typeof(AsksConformanceKick), "e2e-asks")]
    public static Reaction<AsksConformanceState> OnKick(AsksConformanceKick k, AsksConformanceState s, [Slot] Asks<IAsksConformanceProbe> probe)
    {
        var call = probe.Call("Execute", k.Key, JsonSerializer.SerializeToElement(new { mode = k.Mode }));
        if (k.Repeatable)
        {
            call = call with { Repeatable = true, Timeout = TimeSpan.FromSeconds(5) };
        }

        return Reaction<AsksConformanceState>.Of(s).Ask(call);
    }

    [Synapse(typeof(Answered), "e2e-asks")]
    public static AsksConformanceState OnAnswered(Answered all, Answer one, AsksConformanceState s) =>
        s with
        {
            Completed = s.Completed + 1,
            LastKey = one.Key,
            LastResult = one.Ok ? one.Result.GetProperty("outcome").GetString() : null,
            LastError = one.Error,
        };
}

[Module("asks-conformance")]
public sealed class AsksConformanceModule : DigitalBrain.Modules.Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}

public sealed class RecordingProbe : IAsksConformanceProbe
{
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public int AttemptsFor(string mode) => _attempts.GetValueOrDefault(mode);

    public async Task<JsonElement> Execute(string mode, CancellationToken ct = default)
    {
        var attempt = _attempts.AddOrUpdate(mode, 1, (_, n) => n + 1);
        if (mode.StartsWith("fail-once", StringComparison.Ordinal) && attempt == 1)
        {
            throw new InvalidOperationException("transient-probe-error");
        }

        if (string.Equals(mode, "renew-and-finish", StringComparison.Ordinal))
        {
            await AskRun.Current!.RenewLease(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        }

        return JsonSerializer.SerializeToElement(new { outcome = $"{mode}:attempt-{attempt}" });
    }
}

public sealed class AsksProbeConnections(RecordingProbe probe) : ICapabilityConnections
{
    private static readonly BrainConnection Connection = new("probe-conn", "Probe", true, [CapabilityId.Parse("asks-conformance-probe@1")], null);

    public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<BrainConnection>>([Connection]);

    public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class =>
        Task.FromResult((TCapability)(object)probe);

    public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;
}

public sealed class AsksConformanceFixture : BothRuntimesFixture
{
    public RecordingProbe Probe { get; } = new();

    protected override IReadOnlyList<Type> Modules => [typeof(AsksConformanceModule)];

    protected override void ConfigureTestOS(IServiceCollection services) =>
        services.AddSingleton<ICapabilityConnections>(new AsksProbeConnections(Probe));

    protected override void ConfigureSilo(IServiceCollection services) =>
        services.AddSingleton<ICapabilityConnections>(new AsksProbeConnections(Probe));
}

public sealed class AsksConformanceFacts(AsksConformanceFixture fixture) : IClassFixture<AsksConformanceFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task InstallUnit(INeuronRuntime runtime)
    {
        var installs = runtime.Get<IInstalls>(new NeuronId(runtime.Brain, "synapse.installs", "default"));
        if (await installs.Describe("e2e-asks", Ct) is not null)
        {
            return;
        }

        var outcome = await installs.Install(
            new InstallRequest("e2e-asks", new Dictionary<string, string> { ["probe"] = "probe-conn" }),
            Ct);
        if (outcome.Status == InstallStatus.Pending && outcome.Reason is not null)
        {
            await runtime.ApproveAsPerson(outcome.Reason, Ct);
        }

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (await installs.Describe("e2e-asks", Ct) is null && DateTime.UtcNow < deadline)
        {
            await runtime.Idle();
            await Task.Delay(50, Ct);
        }

        Assert.NotNull(await installs.Describe("e2e-asks", Ct));
    }

    private static async Task<IReadOnlyList<Answered>> WaitAnswers(INeuronRuntime runtime, NeuronId asksId, int expectedCount)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (true)
        {
            await runtime.Idle();
            var signals = await runtime.Signals<Answered>(asksId, Ct);
            if (signals.Count >= expectedCount || DateTime.UtcNow > deadline)
            {
                return signals;
            }

            if (runtime.Kind == NeuronRuntimeKind.TestOS)
            {
                await runtime.Elapse(TimeSpan.FromSeconds(1), Ct);
            }
            else
            {
                await Task.Delay(100, Ct);
            }
        }
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task AsksRoundTripRetriesTransientFailureWithRetryPolicyAndDeliversAnswered(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        await InstallUnit(runtime);

        var driver = runtime.Get<IAsksConformanceDriver>(runtime.Id("e2e-asks-driver", "d1"));
        var asksId = new NeuronId(runtime.Brain, AskRules.AsksNeuronType, "e2e-asks");
        var before = (await runtime.Signals<Answered>(asksId, Ct)).Count;

        var mode = $"fail-once-{kind}";
        await driver.Kick($"retry-{kind}", mode, repeatable: true);

        if (kind == NeuronRuntimeKind.TestOS)
        {
            await runtime.Idle();
            // Advance past RetryPolicy.Next(1) = 2s on TestOS manual clock so retry executes.
            await runtime.Elapse(RetryPolicy.Next(1), Ct);
        }

        var answers = await WaitAnswers(runtime, asksId, before + 1);
        var answer = answers.SelectMany(a => a.Answers).FirstOrDefault(a => a.Key == $"retry-{kind}");
        Assert.NotNull(answer);
        Assert.Equal(AnswerStatus.Ok, answer.Status);
        Assert.Equal($"{mode}:attempt-2", answer.Result.GetProperty("outcome").GetString());
        Assert.True(fixture.Probe.AttemptsFor(mode) >= 2);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task AsksLeaseRenewalAndDurableStateSurviveHostRestart(NeuronRuntimeKind kind)
    {
        await using var runtime = await fixture.Start(kind, Ct);
        await InstallUnit(runtime);

        var driver = runtime.Get<IAsksConformanceDriver>(runtime.Id("e2e-asks-driver", "d-restart"));
        var asksId = new NeuronId(runtime.Brain, AskRules.AsksNeuronType, "e2e-asks");

        await driver.Kick("before-restart", "renew-and-finish", repeatable: false);
        var first = await WaitAnswers(runtime, asksId, 1);
        var firstAnswer = Assert.Single(Assert.Single(first).Answers);
        Assert.Equal((AnswerStatus.Ok, "before-restart"), (firstAnswer.Status, firstAnswer.Key));

        await runtime.Restart(Ct);

        await runtime.Get<IAsksConformanceDriver>(runtime.Id("e2e-asks-driver", "d-restart")).Kick("after-restart", "ok", repeatable: false);
        var after = await WaitAnswers(runtime, asksId, 2);
        Assert.Contains(after.SelectMany(a => a.Answers), a => a.Key == "after-restart" && a.Status == AnswerStatus.Ok);
    }
}
