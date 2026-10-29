using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace DigitalBrain.Testing;

/// <summary>
/// Runs the empty-input operation baseline (<see cref="AppBaseline"/>) and every <see cref="ScenarioAttribute"/> method
/// declared on an <see cref="App"/> class (or its nested types), each in an isolated <see cref="TestOS"/> brain, and records
/// the resulting <see cref="BddReport"/> into <see cref="BddNeuron"/> (<see cref="IBdd"/>).
/// </summary>
public static class BddRunner
{
    /// <summary>Verifies <typeparamref name="TApp"/> and optionally records the resulting <see cref="BddReport"/> into <see cref="IBdd"/> on <paramref name="targetOs"/>.</summary>
    public static Task<BddReport> VerifyAsync<TApp>(Action<TestOS>? fakes = null, TestOS? targetOs = null, CancellationToken cancellationToken = default)
        where TApp : App
        => VerifyTypeAsync(typeof(TApp), fakes, targetOs, cancellationToken);

    /// <summary>Verifies <paramref name="appType"/> and optionally records the resulting <see cref="BddReport"/> into <see cref="IBdd"/> on <paramref name="targetOs"/>.</summary>
    public static async Task<BddReport> VerifyTypeAsync(Type appType, Action<TestOS>? fakes = null, TestOS? targetOs = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appType);
        if (!typeof(App).IsAssignableFrom(appType) || appType.IsAbstract)
        {
            throw new ArgumentException($"{appType.FullName} is not a concrete App class.", nameof(appType));
        }

        var totalWatch = Stopwatch.StartNew();
        var manifest = TestOS.ManifestOf(appType);
        var baselinePassed = true;
        string? baselineError = null;

        try
        {
            foreach (var row in AppBaseline.Cases(appType, fakes))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await AppBaseline.RunAsync(row.Data).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            baselinePassed = false;
            baselineError = error.Message;
        }

        var scenarios = new List<ScenarioVerdict>();
        foreach (var (attribute, method) in DiscoverScenarios(appType))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scenarios.Add(await RunScenarioAsync(appType, manifest, attribute.Title, method, fakes, cancellationToken).ConfigureAwait(false));
        }

        var report = new BddReport(manifest.Id, scenarios, baselinePassed, baselineError, totalWatch.Elapsed);
        if (targetOs is not null)
        {
            targetOs.Register<BddNeuron>();
            var bddId = new NeuronId(new BrainId("test"), "bdd", manifest.Id);
            await targetOs.Get<IBdd>(bddId).Record(report, cancellationToken).ConfigureAwait(false);
            await targetOs.Idle().ConfigureAwait(false);
        }

        return report;
    }

    private static IEnumerable<(ScenarioAttribute Attribute, MethodInfo Method)> DiscoverScenarios(Type appType)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        var types = new List<Type> { appType };
        types.AddRange(appType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));
        foreach (var type in types)
        {
            foreach (var method in type.GetMethods(Flags))
            {
                if (method.GetCustomAttribute<ScenarioAttribute>(inherit: false) is { } scenario)
                {
                    yield return (scenario, method);
                }
            }
        }
    }

    private static async Task<ScenarioVerdict> RunScenarioAsync(
        Type appType,
        AppManifest manifest,
        string title,
        MethodInfo method,
        Action<TestOS>? fakes,
        CancellationToken cancellationToken)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            await using var os = TestOS.Local();
            fakes?.Invoke(os);
            foreach (var capability in manifest.Requires.Capabilities)
            {
                if (os.Capabilities.IsBound(capability)) { continue; }
                if (AppBaseline.DefaultFakes.TryGetValue(capability, out var bind))
                {
                    bind(os);
                }
            }

            var install = typeof(TestOS).GetMethod(nameof(TestOS.Install))!.MakeGenericMethod(appType);
            var installTask = (Task)install.Invoke(os, null)!;
            await installTask.ConfigureAwait(false);
            var installed = (IInstalledApp)installTask.GetType().GetProperty(nameof(Task<object>.Result))!.GetValue(installTask)!;

            var target = method.IsStatic
                ? null
                : method.DeclaringType == appType
                    ? installed.Instance
                    : Activator.CreateInstance(method.DeclaringType!);

            var args = BuildScenarioArguments(method.GetParameters(), os, installed, cancellationToken);
            object? result;
            try
            {
                result = method.Invoke(target, args);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }

            switch (result)
            {
                case Task task: await task.ConfigureAwait(false); break;
                case ValueTask valueTask: await valueTask.ConfigureAwait(false); break;
            }

            await os.Idle().ConfigureAwait(false);

            if (os.Failures.Count > 0)
            {
                var failure = os.Failures.First();
                return new ScenarioVerdict(title, false, $"{failure.GetType().Name}: {failure.Message}", watch.Elapsed);
            }

            if (installed.Instance.RecordedError is { } recorded)
            {
                return new ScenarioVerdict(title, false, $"{recorded.Operation}: {recorded.Kind}: {recorded.Message}", watch.Elapsed);
            }

            if (installed.PresentationError is { } presentationError)
            {
                return new ScenarioVerdict(title, false, $"{presentationError.GetType().Name}: {presentationError.Message}", watch.Elapsed);
            }

            return new ScenarioVerdict(title, true, null, watch.Elapsed);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return new ScenarioVerdict(title, false, error.Message, watch.Elapsed);
        }
    }

    private static object?[] BuildScenarioArguments(
        ParameterInfo[] parameters,
        TestOS os,
        IInstalledApp installed,
        CancellationToken cancellationToken)
    {
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            if (paramType == typeof(TestOS))
            {
                args[i] = os;
            }
            else if (paramType.IsInstanceOfType(installed))
            {
                args[i] = installed;
            }
            else if (paramType.IsInstanceOfType(installed.Instance))
            {
                args[i] = installed.Instance;
            }
            else if (paramType == typeof(CancellationToken))
            {
                args[i] = cancellationToken;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Scenario method '{parameters[i].Member.Name}' has unsupported parameter '{parameters[i].Name}' of type '{paramType.FullName}'. "
                    + "Supported parameters are InstalledApp<TApp>, IInstalledApp, TApp, TestOS, and CancellationToken.");
            }
        }
        return args;
    }
}
