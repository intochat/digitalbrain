using System.Diagnostics;
using System.Reflection;
using DigitalBrain.Apps;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Identity;
using Xunit;

namespace DigitalBrain.Testing;

/// <summary>
/// The generated baseline test of an app (D-04, D-11): an xunit <c>[Theory]</c> over the app's manifest with one row per operation.
/// Each row installs the app into a fresh <see cref="TestOS"/> with a fake for every required capability, requests the operation with the
/// empty input, and checks that the app recorded no error state, its views did not throw, and it presents a side or main view, within
/// <see cref="Budget"/>.
/// </summary>
/// <example>
/// <code>
/// public static IEnumerable&lt;TheoryDataRow&lt;AppOperationCase&gt;&gt; Operations => AppBaseline.Cases(typeof(Notes));
///
/// [Theory, MemberData(nameof(Operations))]
/// public Task Baseline(AppOperationCase operation) => AppBaseline.RunAsync(operation);
/// </code>
/// </example>
public static class AppBaseline
{
    /// <summary>How long one row may take, install included.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The capabilities a baseline binds a default fake for when the test binds none: <c>storage@1</c> (<see cref="FakeStorage"/>) and
    /// <c>identity@1</c> (<see cref="InMemoryIdentity"/>). Any other required capability needs a fake from the test, or the row fails.
    /// </summary>
    public static IReadOnlyDictionary<CapabilityId, Action<TestOS>> DefaultFakes { get; } = new Dictionary<CapabilityId, Action<TestOS>>
    {
        [CapabilityFakes.IdOf(typeof(IStorage))] = static os => os.With<IStorage>(new FakeStorage()),
        [CapabilityFakes.IdOf(typeof(IIdentity))] = static os => os.With<IIdentity>(new InMemoryIdentity()),
    };

    /// <summary>One row per <c>[On]</c> operation of the <c>[App]</c> class <paramref name="appType"/>, named <c>app/operation</c>.</summary>
    /// <exception cref="ArgumentException"><paramref name="appType"/> is not a concrete <see cref="App"/> class.</exception>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute or no operation.</exception>
    public static IEnumerable<TheoryDataRow<AppOperationCase>> Cases(Type appType) => Cases(appType, fakes: null);

    /// <summary>One row per <c>[On]</c> operation of the <c>[App]</c> class <paramref name="appType"/>, with fakes bound by <paramref name="fakes"/>.</summary>
    /// <param name="appType">The app class.</param>
    /// <param name="fakes">Binds fakes on each row's <see cref="TestOS"/> (<c>os =&gt; os.With&lt;IMail&gt;(new FakeMail())</c>); null binds only the defaults.</param>
    /// <exception cref="ArgumentException"><paramref name="appType"/> is not a concrete <see cref="App"/> class.</exception>
    /// <exception cref="InvalidOperationException">The class has no <c>[App]</c> attribute or no operation.</exception>
    public static IEnumerable<TheoryDataRow<AppOperationCase>> Cases(Type appType, Action<TestOS>? fakes)
    {
        ArgumentNullException.ThrowIfNull(appType);
        if (!typeof(App).IsAssignableFrom(appType) || appType.IsAbstract)
        {
            throw new ArgumentException($"{appType.FullName} is not a concrete App class.", nameof(appType));
        }
        var manifest = TestOS.ManifestOf(appType);
        var operations = appType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.GetCustomAttribute<OnAttribute>(inherit: false) is { Source: null or "" })
            .Select(method => method.Name)
            .ToList();
        var install = typeof(TestOS).GetMethod(nameof(TestOS.Install))!.MakeGenericMethod(appType);
        return Cases(manifest, operations, async os =>
        {
            var task = (Task)install.Invoke(os, null)!;
            await task.ConfigureAwait(false);
            return (IInstalledApp)task.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(task)!;
        }, fakes);
    }

    /// <summary>
    /// One row per operation of an app installed by <paramref name="install"/>: for apps compiled at run time (script apps), whose
    /// operations come from the spec comment or the compiled class.
    /// </summary>
    /// <exception cref="InvalidOperationException"><paramref name="operations"/> is empty.</exception>
    public static IEnumerable<TheoryDataRow<AppOperationCase>> Cases(
        AppManifest manifest, IEnumerable<string> operations, Func<TestOS, Task<IInstalledApp>> install, Action<TestOS>? fakes)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(install);
        var names = operations.Select(SpecComment.NormalizeOperation).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (names.Count == 0)
        {
            throw new InvalidOperationException($"App '{manifest.Id}' has no operation: an app has at least one [On] method.");
        }
        var opens = names.Contains("open", StringComparer.Ordinal);
        return [.. names.Select(name =>
        {
            var row = new AppOperationCase(manifest, name, install, fakes, opens && name != "open");
            return new TheoryDataRow<AppOperationCase>(row) { TestDisplayName = row.DisplayName };
        })];
    }

    /// <summary>
    /// Runs one baseline row. Fails with <see cref="AppBaselineException"/> naming the app and the operation when a required capability
    /// has no fake, the app throws, records an error state, its views throw, it presents nothing, or the row takes longer than
    /// <see cref="Budget"/>.
    /// </summary>
    public static async Task RunAsync(AppOperationCase operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var watch = Stopwatch.StartNew();
        var name = operation.DisplayName;
        await using var os = TestOS.Local();
        operation.Fakes?.Invoke(os);
        foreach (var capability in operation.Manifest.Requires.Capabilities)
        {
            if (os.Capabilities.IsBound(capability)) { continue; }
            if (!DefaultFakes.TryGetValue(capability, out var bind))
            {
                throw new AppBaselineException(
                    $"Baseline {name}: the app requires capability {capability} and no fake is bound for it. "
                    + $"Pass one to AppBaseline.Cases(..., fakes: os => os.With<I...>(fake)).");
            }
            bind(os);
        }

        IInstalledApp app;
        try { app = await Within(operation.Install(os), name, "install", watch).ConfigureAwait(false); }
        catch (CapabilityNotBoundException error)
        {
            throw new AppBaselineException($"Baseline {name}: {error.Message}", error);
        }
        if (operation.OpensFirst) { await Request(app, "open", name, watch).ConfigureAwait(false); }
        await Request(app, operation.Operation, name, watch).ConfigureAwait(false);
        await Within(os.Idle(), name, "idle", watch).ConfigureAwait(false);

        if (!(os.Failures.Count == 0))
        {
            var failure = os.Failures.First();
            throw new AppBaselineException($"Baseline {name}: a neuron failed: {failure.GetType().Name}: {failure.Message}", failure);
        }
        if (app.Instance.RecordedError is { } recorded)
        {
            throw new AppBaselineException(
                $"Baseline {name}: the app recorded an error state in '{recorded.Operation}': {recorded.Kind}: {recorded.Message}. "
                + "Empty input should give a typed validation state, not an error.");
        }
        if (app.PresentationError is { } broken)
        {
            throw new AppBaselineException($"Baseline {name}: a view threw: {broken.GetType().Name}: {broken.Message}", broken);
        }
        if (app.Presentation is { Side: null, Main: null })
        {
            throw new AppBaselineException($"Baseline {name}: the app presented neither a side nor a main view.");
        }
        if (watch.Elapsed > Budget)
        {
            throw new AppBaselineException($"Baseline {name}: took {watch.Elapsed.TotalSeconds:F1} s, over the {Budget.TotalSeconds:F0} s budget.");
        }
    }

    private static async Task Request(IInstalledApp app, string operation, string name, Stopwatch watch)
    {
        try { await Within(app.Request(operation), name, operation, watch).ConfigureAwait(false); }
        catch (AppBaselineException) { throw; }
        catch (Exception error)
        {
            throw new AppBaselineException(
                $"Baseline {name}: requesting '{operation}' with the empty input threw {error.GetType().Name}: {error.Message}", error);
        }
    }

    private static async Task Within(Task task, string name, string step, Stopwatch watch)
    {
        var left = Budget - watch.Elapsed;
        try { await task.WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.Zero).ConfigureAwait(false); }
        catch (TimeoutException error)
        {
            throw new AppBaselineException($"Baseline {name}: '{step}' did not finish within the {Budget.TotalSeconds:F0} s budget.", error);
        }
    }

    private static async Task<T> Within<T>(Task<T> task, string name, string step, Stopwatch watch)
    {
        await Within((Task)task, name, step, watch).ConfigureAwait(false);
        return await task.ConfigureAwait(false);
    }
}
