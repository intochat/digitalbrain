using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Testing;

/// <summary>
/// A single baseline test case for an [On] signal handled by a screen.
/// </summary>
public sealed record ScreenOperationCase(Type ScreenType, string Signal, Action<TestOS>? Fakes = null)
{
    /// <summary>Human-readable display name for theory runners.</summary>
    public string DisplayName => $"{ScreenType.Name}/{Signal}";

    /// <inheritdoc />
    public override string ToString() => DisplayName;
}

/// <summary>A failure observed during a screen baseline execution.</summary>
public sealed class ScreenBaselineException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Automated baseline theory runner for screen classes.
/// </summary>
public static class ScreenBaseline
{
    /// <summary>How long one row may take. Generous enough for a full-solution run on a loaded machine.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(30);

    /// <summary>One row per [On] reaction of <paramref name="screenType"/>.</summary>
    public static IEnumerable<TheoryDataRow<ScreenOperationCase>> Cases(Type screenType) => Cases(screenType, fakes: null);

    /// <summary>One row per [On] reaction of <paramref name="screenType"/> with fakes bound by <paramref name="fakes"/>.</summary>
    public static IEnumerable<TheoryDataRow<ScreenOperationCase>> Cases(Type screenType, Action<TestOS>? fakes)
    {
        ArgumentNullException.ThrowIfNull(screenType);
        if (screenType.IsAbstract)
        {
            throw new ArgumentException($"{screenType.FullName} is abstract; screen baselines test concrete screen classes.", nameof(screenType));
        }

        var signals = ScreenSignals.For(screenType);
        if (signals.Count == 0)
        {
            throw new InvalidOperationException($"Screen '{screenType.Name}' has no [On] reactions.");
        }

        return signals.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(signal =>
        {
            var testCase = new ScreenOperationCase(screenType, signal, fakes);
            return new TheoryDataRow<ScreenOperationCase>(testCase) { TestDisplayName = testCase.DisplayName };
        });
    }

    /// <summary>
    /// Executes one baseline row: ensures initial view exists, requests the signal with empty input, asserts non-null view,
    /// verifies state inspectability round-trip, and validates activation refusal invariants.
    /// </summary>
    public static async Task RunAsync(ScreenOperationCase operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var watch = Stopwatch.StartNew();
        var name = operation.DisplayName;

        await using var os = TestOS.Local();
        operation.Fakes?.Invoke(os);
        os.Register(operation.ScreenType.Assembly);

        var screenAlias = PlainNeuronContracts.TypeOf(operation.ScreenType)
            ?? operation.ScreenType.GetCustomAttribute<NeuronAttribute>()?.Type
            ?? operation.ScreenType.Name;
        var screenId = new NeuronId(new BrainId("test"), screenAlias, "default");
        var screen = os.Get<IScreen>(screenId);

        // 1. Initial activation: Read should return the initial published view
        var initialView = await Within(screen.Read(), name, "initial read", watch).ConfigureAwait(false);
        if (initialView is null)
        {
            throw new ScreenBaselineException($"Baseline {name}: the screen presented no initial view upon activation.");
        }

        // 2. Request the operation signal with empty JSON object {}
        try
        {
            using var emptyDoc = JsonDocument.Parse("{}");
            await Within(screen.Request(operation.Signal, emptyDoc.RootElement), name, operation.Signal, watch).ConfigureAwait(false);
        }
        catch (ScreenBaselineException) { throw; }
        catch (Exception error)
        {
            throw new ScreenBaselineException($"Baseline {name}: requesting '{operation.Signal}' with empty input threw {error.GetType().Name}: {error.Message}", error);
        }

        await Within(os.Idle(), name, "idle", watch).ConfigureAwait(false);

        if (os.Failures.Count > 0)
        {
            var failure = os.Failures.First();
            throw new ScreenBaselineException($"Baseline {name}: a neuron failed: {failure.GetType().Name}: {failure.Message}", failure);
        }

        // 3. View assertion
        var currentView = await screen.Read().ConfigureAwait(false);
        if (currentView is null)
        {
            throw new ScreenBaselineException($"Baseline {name}: the screen presented no view after request.");
        }

        // 4. State round-trip check
        if (os.Get<IStateInspectable>(screenId) is { } inspectable)
        {
            var inspected = await inspectable.InspectState(CancellationToken.None).ConfigureAwait(false);
            if (inspected is null)
            {
                throw new ScreenBaselineException($"Baseline {name}: inspectable state returned null.");
            }
        }

        // 5. Activation refusal invariant checks
        await Assert.ThrowsAsync<ActivationRefusedException>(() => screen.Activate(currentView.Alias, currentView.Generation - 1, "nonexistent-node-key")).ConfigureAwait(false);
        await Assert.ThrowsAsync<ActivationRefusedException>(() => screen.Activate(currentView.Alias, currentView.Generation, "nonexistent-node-key")).ConfigureAwait(false);

        if (watch.Elapsed > Budget)
        {
            throw new ScreenBaselineException($"Baseline {name}: took {watch.Elapsed.TotalSeconds:F1} s, exceeding the {Budget.TotalSeconds:F0} s budget.");
        }
    }

    private static async Task Within(Task task, string name, string step, Stopwatch watch)
    {
        var left = Budget - watch.Elapsed;
        try { await task.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero).ConfigureAwait(false); }
        catch (TimeoutException error)
        {
            throw new ScreenBaselineException($"Baseline {name}: '{step}' did not finish within the {Budget.TotalSeconds:F0} s budget.", error);
        }
    }

    private static async Task<T> Within<T>(Task<T> task, string name, string step, Stopwatch watch)
    {
        await Within((Task)task, name, step, watch).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
}
