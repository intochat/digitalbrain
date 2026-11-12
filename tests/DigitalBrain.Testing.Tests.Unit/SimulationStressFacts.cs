using DigitalBrain.Contracts;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Http;
using DigitalBrain.Invocation;
using DigitalBrain.Testing.Synapses;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit;

// Adversarial stress and edge-case challenge suite for DigitalBrain.Simulation (Feature F10)
public sealed class SimulationStressFacts
{
    private static readonly BrainId StressBrain = new("stress-brain");

    #region 1. Assembly Purity and Symbol Decoupling

    [Fact]
    public void SimulationAssembly_ReferencesZeroXUnitAndZeroOrleans()
    {
        var simulationAssembly = typeof(TestOS).Assembly;
        var referencedAssemblies = simulationAssembly.GetReferencedAssemblies();

        Assert.NotEmpty(referencedAssemblies);

        // Verify zero xUnit references
        Assert.DoesNotContain(referencedAssemblies, r =>
            r.Name!.StartsWith("xunit", StringComparison.OrdinalIgnoreCase));

        // Verify zero Orleans references
        Assert.DoesNotContain(referencedAssemblies, r =>
            r.Name!.StartsWith("Orleans", StringComparison.OrdinalIgnoreCase) ||
            r.Name.StartsWith("Microsoft.Orleans", StringComparison.OrdinalIgnoreCase));

        // Verify zero testing harness references
        Assert.DoesNotContain(referencedAssemblies, r =>
            r.Name!.Equals("DigitalBrain.Testing", StringComparison.Ordinal) ||
            r.Name.Equals("DigitalBrain.Testing.Silo", StringComparison.Ordinal) ||
            r.Name.StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal));
    }

    [Fact]
    public void SimulationAssembly_TypesDeclareNoOrleansOrTestRunnerAttributes()
    {
        var simulationAssembly = typeof(TestOS).Assembly;
        var types = simulationAssembly.GetTypes();

        Assert.NotEmpty(types);

        foreach (var type in types)
        {
            // Verify namespace does not leak Orleans or runner
            Assert.False(type.Namespace is { } ns && (ns.Contains("Orleans") || ns.Contains("xunit", StringComparison.OrdinalIgnoreCase)),
                $"Type {type.FullName} declares disallowed namespace.");

            // Verify no xUnit attribute or inheritance
            var baseType = type.BaseType;
            while (baseType is not null)
            {
                Assert.False(baseType.FullName?.Contains("xunit", StringComparison.OrdinalIgnoreCase) == true,
                    $"Type {type.FullName} derives from test framework type {baseType.FullName}.");
                baseType = baseType.BaseType;
            }
        }
    }

    [Fact]
    public void NeuronAssertionException_IsPureCustomException_NotXUnitSdk()
    {
        var exType = typeof(NeuronAssertionException);
        Assert.True(typeof(Exception).IsAssignableFrom(exType));
        Assert.Equal(typeof(Exception), exType.BaseType);

        var asmName = exType.Assembly.GetName().Name;
        Assert.Equal("DigitalBrain.Simulation", asmName);

        var instance = new NeuronAssertionException("test message", new InvalidOperationException("inner"));
        Assert.Equal("test message", instance.Message);
        Assert.IsType<InvalidOperationException>(instance.InnerException);
    }

    #endregion

    #region 2. Virtual Clock Edge Cases & Stress

    [Fact]
    public void VirtualClock_ClockFreezing_TimersDoNotFireUntilAdvanced()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var firedCount = 0;

        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref firedCount), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        // Frozen clock: zero advancement
        Assert.Equal(0, Volatile.Read(ref firedCount));

        // Advance just before due time
        clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal(0, Volatile.Read(ref firedCount));

        // Advance to exact due time
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref firedCount));

        // Advance beyond next period
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(3, Volatile.Read(ref firedCount));
    }

    [Fact]
    public void VirtualClock_NegativeAdvance_ThrowsArgumentOutOfRangeException()
    {
        var clock = new TestClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void VirtualClock_ZeroAdvance_ExecutesPendingDueTimersOnly()
    {
        var clock = new TestClock();
        var fired = false;

        using var timer = clock.CreateTimer(_ => fired = true, null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

        // Timer is due immediately at now; Advance(0) should fire it
        clock.Advance(TimeSpan.Zero);
        Assert.True(fired);
    }

    [Fact]
    public void VirtualClock_FastForwarding_HandlesLongSpansWithoutLoss()
    {
        var clock = new TestClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var ticks = 0;

        // Periodic timer: every 1 hour
        using var timer = clock.CreateTimer(_ => Interlocked.Increment(ref ticks), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        // Fast-forward 100 days (2400 hours)
        clock.Advance(TimeSpan.FromDays(100));
        Assert.Equal(2400, Volatile.Read(ref ticks));
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromDays(100), clock.GetUtcNow());
    }

    [Fact]
    public void VirtualClock_MultipleTimersSameDue_PreservesStableFifoOrder()
    {
        var clock = new TestClock();
        var executionLog = new List<int>();

        using var t1 = clock.CreateTimer(_ => executionLog.Add(1), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        using var t2 = clock.CreateTimer(_ => executionLog.Add(2), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        using var t3 = clock.CreateTimer(_ => executionLog.Add(3), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal([1, 2, 3], executionLog);
    }

    [Fact]
    public void VirtualClock_DisposedTimer_DoesNotFireOnSubsequentAdvance()
    {
        var clock = new TestClock();
        var fired = false;

        var timer = clock.CreateTimer(_ => fired = true, null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.False(fired);
    }

    #endregion

    #region 3. Simulation Execution & Concurrency Stress

    public sealed record StressCounterState
    {
        public int Value { get; init; }
    }

    public sealed record StressIncrementSignal(int NewValue) : Signal;

    public interface IStressNeuron
    {
        Task<int> Increment();
        Task<int> GetValue();
        Task HangIndefinitely();
    }

    [Neuron("stress.worker")]
    public sealed class StressNeuron : Neuron<StressCounterState>, IStressNeuron
    {
        private static readonly TaskCompletionSource s_hangTcs = new();

        public static void ReleaseHang() => s_hangTcs.TrySetResult();

        public async Task<int> Increment()
        {
            var next = State.Value + 1;
            await Save(State with { Value = next }, new StressIncrementSignal(next));
            return next;
        }

        public Task<int> GetValue() => Task.FromResult(State.Value);

        public async Task HangIndefinitely()
        {
            await s_hangTcs.Task;
        }
    }

    [Fact]
    public async Task TestOS_SimultaneousTurnsOnSameNeuron_SerializedDeterministically()
    {
        await using var os = OS.Local();
        os.Register<StressNeuron>();

        var id = new NeuronId(StressBrain, "stress.worker", "singleton");
        var proxy = os.Get<IStressNeuron>(id);

        const int ConcurrentCalls = 50;
        var tasks = Enumerable.Range(0, ConcurrentCalls).Select(_ => proxy.Increment()).ToArray();
        var results = await Task.WhenAll(tasks);

        await os.Idle();

        Assert.Equal(ConcurrentCalls, await proxy.GetValue());
        // All intermediate increments from 1..50 should be observed
        Assert.Equal(Enumerable.Range(1, ConcurrentCalls), results.OrderBy(x => x));

        // Verify journal holds exactly 50 commits
        var journal = os.Journal(StressBrain);
        Assert.Equal(ConcurrentCalls, journal.Signals(id).Count);
    }

    [Fact]
    public async Task TestOS_ConcurrentTurnsAcrossMultipleNeurons_ExecuteIndependently()
    {
        await using var os = OS.Local();
        os.Register<StressNeuron>();

        const int NeuronCount = 20;
        var neuronIds = Enumerable.Range(0, NeuronCount)
            .Select(i => new NeuronId(StressBrain, "stress.worker", $"worker-{i}"))
            .ToArray();

        var tasks = neuronIds.Select(async id =>
        {
            var proxy = os.Get<IStressNeuron>(id);
            await proxy.Increment();
            await proxy.Increment();
            return await proxy.GetValue();
        });

        var results = await Task.WhenAll(tasks);
        await os.Idle();

        Assert.All(results, val => Assert.Equal(2, val));
    }

    [Fact]
    public async Task TestOS_HangingTurn_TimesOutOnVirtualClockCallTimeout()
    {
        await using var os = OS.Local(opts => opts.CallTimeout = TimeSpan.FromSeconds(5));
        os.Register<StressNeuron>();

        var id = new NeuronId(StressBrain, "stress.worker", "hanging");
        var proxy = os.Get<IStressNeuron>(id);

        try
        {
            var callTask = proxy.HangIndefinitely();

            for (var step = 0; step < 50 && !callTask.IsCompleted; step++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                os.Clock.Advance(os.Options.CallTimeout);
            }

            var ex = await Assert.ThrowsAsync<TimeoutException>(() => callTask);
            Assert.NotNull(ex);
        }
        finally
        {
            StressNeuron.ReleaseHang();
            await os.Idle();
        }
    }

    #endregion

    #region 4. Custom Factories & Dependency Injection

    public interface ICustomService
    {
        string Name { get; }
    }

    public sealed class CustomService : ICustomService
    {
        public string Name => "StressInjectedService";
    }

    public interface IServiceNeuron
    {
        Task<string> GetServiceName();
    }

    [Neuron("stress.di")]
    public sealed class ServiceNeuron(ICustomService service) : Neuron, IServiceNeuron
    {
        public Task<string> GetServiceName() => Task.FromResult(service.Name);
    }

    public interface INeedyNeuron
    {
        Task Ping();
    }

    public interface IMissingDependency;

    [Neuron("stress.needy")]
    public sealed class NeedyNeuron(IMissingDependency missing) : Neuron, INeedyNeuron
    {
        public IMissingDependency Missing => missing;
        public Task Ping() => Task.CompletedTask;
    }

    private sealed class CustomServiceProvider(ICustomService service) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(ICustomService) ? service : null;
    }

    [Fact]
    public async Task TestOS_CustomServiceFactory_ResolvesConstructorDependencies()
    {
        var services = new CustomServiceProvider(new CustomService());
        await using var os = OS.Local(opts => opts.Services = services);
        os.Register<ServiceNeuron>();

        var id = new NeuronId(StressBrain, "stress.di", "di-test");
        var proxy = os.Get<IServiceNeuron>(id);

        var name = await proxy.GetServiceName();
        Assert.Equal("StressInjectedService", name);
    }

    [Fact]
    public async Task TestOS_MissingConstructorDependency_ThrowsDescriptiveInvalidOperationException()
    {
        await using var os = OS.Local();
        os.Register<NeedyNeuron>();

        var id = new NeuronId(StressBrain, "stress.needy", "fail-test");
        var proxy = os.Get<INeedyNeuron>(id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => proxy.Ping());
        Assert.Contains("needs a IMissingDependency", ex.Message, StringComparison.Ordinal);
    }

    #endregion

    #region 5. NeuronHarness, Unseated State, and NeuronAssertionException

    public sealed record HarnessState
    {
        public string Content { get; init; } = "default";
    }

    public sealed record SampleSignal(string Text) : Signal;

    [Neuron("stress.harness")]
    public sealed class HarnessNeuron : Neuron<HarnessState>
    {
        public async Task Mutate(string text)
        {
            await Save(new HarnessState { Content = text }, new SampleSignal(text));
        }
    }

    [Fact]
    public async Task NeuronHarness_UnseatedState_LoadsDefaultBeforeFirstSave()
    {
        var id = new NeuronId(StressBrain, "stress.harness", "harness-1");
        var harness = await NeuronHarness.Create<HarnessNeuron>(id, TestContext.Current.CancellationToken);

        // Context state has not been written to memory yet
        Assert.False(harness.Context.MemoryState.Exists);
        Assert.Null(await harness.Context.MemoryState.Load<HarnessState>());

        // Nothing published initially
        harness.AssertNothingPublished();

        // Mutate state and assert
        await harness.Neuron.Mutate("new-content");
        Assert.True(harness.Context.MemoryState.Exists);
        Assert.Equal(1, harness.Context.MemoryState.Saves);

        var published = harness.AssertPublished<SampleSignal>(s => s.Text == "new-content");
        Assert.Equal("new-content", published.Text);

        // AssertNothingPublished should now throw NeuronAssertionException
        var ex = Assert.Throws<NeuronAssertionException>(harness.AssertNothingPublished);
        Assert.Contains("Expected no signals. Published: [SampleSignal", ex.Message, StringComparison.Ordinal);

        // AssertPublished for nonexistent signal throws NeuronAssertionException
        var exNotFound = Assert.Throws<NeuronAssertionException>(() => harness.AssertPublished<SampleSignal>(s => s.Text == "absent"));
        Assert.Contains("No published SampleSignal matched", exNotFound.Message, StringComparison.Ordinal);
    }

    #endregion

    #region 6. UnitScenario & Transcript Assertions

    private const string SimpleScenarioSource = """
        namespace Stress.Scenario;

        [Signal("stress.ping")]
        public sealed record Ping(int Seq) : Signal;

        [State("stress.unit")]
        public sealed record UnitState(int Total = 0)
        {
            public UnitState() : this(0) { }
        }

        public static class ScenarioLogic
        {
            [Synapse("stress.ping", "stress.unit")]
            public static UnitState OnPing(SignalEnvelope s, UnitState u) =>
                u with { Total = u.Total + 1 };
        }
        """;

    [Fact]
    public async Task UnitScenario_InvalidStepTick_ProducesFailureTranscriptWithoutCrashing()
    {
        var source = new SynapseSource("Stress.Scenario", new Dictionary<string, string> { ["Source.cs"] = SimpleScenarioSource }, null, null);
        var fakes = new FakeSlots();

        // Step with Tick At <= now (start of clock is 2026-01-01)
        var pastTick = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var steps = new ScenarioStep[]
        {
            new Tick(pastTick)
        };

        var transcript = await UnitScenario.Run(source, fakes, steps, TestContext.Current.CancellationToken);

        Assert.False(transcript.Passed);
        Assert.NotEmpty(transcript.Failures);
        Assert.Contains(transcript.Failures, f => f.Contains("Tick: At is not after now", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnitScenario_ExpectStateOnNonExistentProperty_FailsGracefullyInTranscript()
    {
        var source = new SynapseSource("Stress.Scenario", new Dictionary<string, string> { ["Source.cs"] = SimpleScenarioSource }, null, null);
        var fakes = new FakeSlots();

        var steps = new ScenarioStep[]
        {
            new ExpectState("stress.unit", "NonExistentField", JsonElement.Parse("\"some-val\""))
        };

        var transcript = await UnitScenario.Run(source, fakes, steps, TestContext.Current.CancellationToken);

        Assert.False(transcript.Passed);
        Assert.NotEmpty(transcript.Failures);
        Assert.Contains(transcript.Failures, f => f.Contains("ExpectState", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnitScenario_ExpectSignalWhenNotPublished_RecordsFailure()
    {
        var source = new SynapseSource("Stress.Scenario", new Dictionary<string, string> { ["Source.cs"] = SimpleScenarioSource }, null, null);
        var fakes = new FakeSlots();

        var steps = new ScenarioStep[]
        {
            new ExpectSignal("stress.never_emitted")
        };

        var transcript = await UnitScenario.Run(source, fakes, steps, TestContext.Current.CancellationToken);

        Assert.False(transcript.Passed);
        Assert.Contains(transcript.Failures, f => f.Contains("ExpectSignal stress.never_emitted: no such signal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnitScenario_CompilationFailure_ReturnsFailedTranscriptWithDiagnostics()
    {
        const string BrokenSyntaxSource = """
            namespace Stress.Broken;
            class This is completely invalid syntax {}
            """;

        var source = new SynapseSource("Stress.Broken", new Dictionary<string, string> { ["Broken.cs"] = BrokenSyntaxSource }, null, null);
        var fakes = new FakeSlots();

        var transcript = await UnitScenario.Run(source, fakes, [], TestContext.Current.CancellationToken);

        Assert.False(transcript.Passed);
        Assert.NotEmpty(transcript.Failures);
        Assert.Contains(transcript.Failures, f => f.Contains("compile", StringComparison.OrdinalIgnoreCase));
    }

    #endregion

    #region 7. Deep Edge Cases (Virtual Clock, Seeded State, Restarts, Publish Faults)

    [Fact]
    public void VirtualClock_TimerException_PropagatesOutOfAdvance()
    {
        var clock = new TestClock();
        using var timer = clock.CreateTimer(_ => throw new InvalidOperationException("Boom in timer"), null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);

        var ex = Assert.Throws<InvalidOperationException>(() => clock.Advance(TimeSpan.FromSeconds(10)));
        Assert.Equal("Boom in timer", ex.Message);
    }

    [Fact]
    public void VirtualClock_TimerRescheduling_AltersExecutionTiming()
    {
        var clock = new TestClock();
        var executionLog = new List<string>();

        using var timer = clock.CreateTimer(_ => executionLog.Add("fired"), null, TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);

        // Advance 5 seconds: not fired yet
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(executionLog);

        // Reschedule to 20 seconds from now
        timer.Change(TimeSpan.FromSeconds(20), Timeout.InfiniteTimeSpan);

        // Advance 10 more seconds: total 15, should still not fire
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Empty(executionLog);

        // Advance 10 more seconds: total 25, now fired
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(["fired"], executionLog);
    }

    [Fact]
    public async Task TestOS_SeedStoredState_SeedsStateBeforeActivation()
    {
        await using var os = OS.Local();
        os.Register<StressNeuron>();

        var id = new NeuronId(StressBrain, "stress.worker", "seeded-1");

        // Seed raw state directly into journal before neuron activates
        os.SeedStoredState(id, """{"value": 999}""");

        var proxy = os.Get<IStressNeuron>(id);
        var value = await proxy.GetValue();

        Assert.Equal(999, value);
    }

    [Fact]
    public async Task TestOS_Restart_PreservesJournalAndStateAcrossHostLifecycles()
    {
        await using var os = OS.Local();
        os.Register<StressNeuron>();

        var id = new NeuronId(StressBrain, "stress.worker", "restart-test");
        var proxy = os.Get<IStressNeuron>(id);

        await proxy.Increment();
        Assert.Equal(1, await proxy.GetValue());

        // Restart TestOS
        await os.Restart();

        // After restart, neuron is reactivated and state survives
        var afterRestart = os.Get<IStressNeuron>(id);
        Assert.Equal(1, await afterRestart.GetValue());

        // Increment again
        var next = await afterRestart.Increment();
        Assert.Equal(2, next);
    }

    #endregion
}
