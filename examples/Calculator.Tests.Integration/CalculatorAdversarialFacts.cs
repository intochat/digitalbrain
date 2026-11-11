using System.Collections.Concurrent;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Examples.Calculator;
using DigitalBrain.Identity.Local;
using DigitalBrain.Neurons;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using DigitalBrain.Units;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Calculator.Tests.Integration;

public sealed record InteractiveState
{
    public int Count { get; init; }
}

[Signal("interactive.increment")]
public sealed record InteractiveIncrement(int Amount = 1) : Signal;

[Neuron("app.interactive-adversarial")]
public sealed class InteractiveAdversarialScreen : Screen<InteractiveState>
{
    protected override View Render(InteractiveState state)
        => new("Interactive", new Column([
            new Heading($"Count: {state.Count}"),
            new Button("btn-inc", "Increment", Emits(new InteractiveIncrement(1)))
        ]));

    [On]
    public Task When(InteractiveIncrement signal) => Show(State with { Count = State.Count + signal.Amount });
}

public sealed class AdversarialRuntimeFixture : BothRuntimesFixture
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "db-adv-" + Guid.NewGuid().ToString("N"));

    protected override IReadOnlyList<Type> Modules => [typeof(CalculatorScreen), typeof(InteractiveAdversarialScreen)];

    protected override void ConfigureSilo(IServiceCollection services)
        => services.AddLocalIdentity(SiloNeurons.Get, _home);
}

public sealed class CalculatorAdversarialFacts(AdversarialRuntimeFixture fixture) : IClassFixture<AdversarialRuntimeFixture>
{
    // =========================================================================
    // 1. Concurrency & Serial Turn Execution via Orleans Mailbox
    // =========================================================================

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ConcurrentRequests_FiftyPressesSerializedWithoutRaceCondition(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var screenId = runtime.Id("app.calculator", "fifty-presses-" + Guid.NewGuid().ToString("N"));
        var screen = runtime.Get<IScreen>(screenId);

        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);

        // Fire 50 concurrent Request<CalculatorPress>("1") calls across threads
        const int count = 50;
        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Run(() => screen.Request<CalculatorPress>(new CalculatorPress("1"))))
            .ToArray();

        await Task.WhenAll(tasks);
        await runtime.Idle();

        var finalView = await screen.Read();
        Assert.NotNull(finalView);

        // Each press appends '1' to the expression, so 50 presses must result in exactly 50 '1's:
        var rawText = finalView.View.GetRawText();
        var expectedExpression = new string('1', count);
        Assert.Contains(expectedExpression, rawText);

        // Generation must have incremented exactly 50 times from 1 to 51:
        Assert.Equal(1 + count, finalView.Generation);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ConcurrentRequests_OneHundredPressesSerializedWithoutRaceCondition(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var screenId = runtime.Id("app.calculator", "hundred-presses-" + Guid.NewGuid().ToString("N"));
        var screen = runtime.Get<IScreen>(screenId);

        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);

        // Fire 100 concurrent Request<CalculatorPress>("2") calls across threads
        const int count = 100;
        using var barrier = new Barrier(count);
        var tasks = Enumerable.Range(0, count)
            .Select(_ => Task.Run(async () =>
            {
                barrier.SignalAndWait(TestContext.Current.CancellationToken);
                await screen.Request<CalculatorPress>(new CalculatorPress("2"));
            }, TestContext.Current.CancellationToken))
            .ToArray();

        await Task.WhenAll(tasks);
        await runtime.Idle();

        var finalView = await screen.Read();
        Assert.NotNull(finalView);

        // Each press appends '2' to the expression, so 100 presses must result in exactly 100 '2's:
        var rawText = finalView.View.GetRawText();
        var expectedExpression = new string('2', count);
        Assert.Contains(expectedExpression, rawText);

        // Generation must have incremented exactly 100 times from 1 to 101:
        Assert.Equal(1 + count, finalView.Generation);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ConcurrentActivations_OptimisticConcurrency_ExactlyOneSucceeds(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var screenId = runtime.Id("app.interactive-adversarial", "activation-race-" + Guid.NewGuid().ToString("N"));
        var screen = runtime.Get<IScreen>(screenId);

        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);
        Assert.Contains("Count: 0", initialView.View.GetRawText());

        const int concurrentCallers = 20;
        var successes = new ConcurrentBag<int>();
        var staleExceptions = new ConcurrentBag<ActivationRefusedException>();
        var otherExceptions = new ConcurrentBag<Exception>();

        var tasks = Enumerable.Range(0, concurrentCallers).Select(i => Task.Run(async () =>
        {
            try
            {
                // All 20 callers attempt to activate using the SAME initial generation 1
                await screen.Activate("app.interactive-adversarial", initialView.Generation, "btn-inc");
                successes.Add(i);
            }
            catch (ActivationRefusedException ex) when (ex.Reason == ActivationRefusedException.Stale)
            {
                staleExceptions.Add(ex);
            }
            catch (Exception ex)
            {
                otherExceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);
        await runtime.Idle();

        // INVARIANT CHECK:
        // Exactly ONE caller must succeed
        Assert.Single(successes);

        // Exactly (concurrentCallers - 1) callers must receive ActivationRefusedException.Stale
        Assert.Equal(concurrentCallers - 1, staleExceptions.Count);

        // No unhandled exceptions or crashes
        Assert.Empty(otherExceptions);

        // View generation must be exactly 2
        var finalView = await screen.Read();
        Assert.NotNull(finalView);
        Assert.Equal(2, finalView.Generation);

        // Count must be 1 (incremented exactly ONCE, not multiple times)
        var raw = finalView.View.GetRawText();
        Assert.Contains("Count: 1", raw);
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task ChainedSequentialActivations_IncrementsGenerationPredictably(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var screenId = runtime.Id("app.interactive-adversarial", "chained-activations-" + Guid.NewGuid().ToString("N"));
        var screen = runtime.Get<IScreen>(screenId);

        var view = await screen.Read();
        Assert.NotNull(view);
        Assert.Equal(1, view.Generation);

        // Perform 5 properly sequenced activations: Count goes 0 -> 1 -> 2 -> 3 -> 4 -> 5
        const int steps = 5;
        for (var i = 0; i < steps; i++)
        {
            await screen.Activate("app.interactive-adversarial", view.Generation, "btn-inc");
            view = await screen.Read();
            Assert.NotNull(view);
            Assert.Equal(2 + i, view.Generation);
            Assert.Contains($"Count: {i + 1}", view.View.GetRawText());
        }
    }

    [Theory, MemberData(nameof(NeuronRuntimeKinds.Both), MemberType = typeof(NeuronRuntimeKinds))]
    public async Task Durability_StateAndGenerationSurvive(NeuronRuntimeKind kind)
    {
        var runtime = await fixture.Runtime(kind);
        var screenId = runtime.Id("app.interactive-adversarial", "durability-" + Guid.NewGuid().ToString("N"));
        var screen = runtime.Get<IScreen>(screenId);

        var view0 = await screen.Read();
        Assert.NotNull(view0);
        Assert.Equal(1, view0.Generation);
        Assert.Contains("Count: 0", view0.View.GetRawText());

        // Perform 3 sequential activations
        await screen.Activate("app.interactive-adversarial", 1, "btn-inc");
        var view1 = await screen.Read();
        Assert.NotNull(view1);
        Assert.Equal(2, view1.Generation);
        Assert.Contains("Count: 1", view1.View.GetRawText());

        await screen.Activate("app.interactive-adversarial", 2, "btn-inc");
        var view2 = await screen.Read();
        Assert.NotNull(view2);
        Assert.Equal(3, view2.Generation);
        Assert.Contains("Count: 2", view2.View.GetRawText());

        await screen.Activate("app.interactive-adversarial", 3, "btn-inc");
        var view3 = await screen.Read();
        Assert.NotNull(view3);
        Assert.Equal(4, view3.Generation);
        Assert.Contains("Count: 3", view3.View.GetRawText());

        // Read again through fresh proxy lookup to verify state & view generation persistence
        var reloadedScreen = runtime.Get<IScreen>(screenId);
        var reloadedView = await reloadedScreen.Read();
        Assert.NotNull(reloadedView);
        Assert.Equal(4, reloadedView.Generation);
        Assert.Contains("Count: 3", reloadedView.View.GetRawText());
    }
}
