using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;

namespace DigitalBrain.Behaviors;

internal sealed class InstallTestsBddGate : IBehaviorBddGate
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "BDD gate failures become typed install reports, never unhandled silo exceptions.")]
    public BehaviorInstallTestReport Evaluate(
        BehaviorArtifactEnvelope envelope,
        ReadOnlyMemory<byte> assemblyBytes,
        string artifactHash,
        IBehaviorCapabilityResolver capabilities,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(time);

        var binding = BehaviorScenarioBinder.Bind(
            envelope.FeatureSource,
            envelope.Manifest.Scenarios);
        if (!binding.Passed)
        {
            return BehaviorInstallTestReport.Fail(binding.Detail, binding.ScenarioCount);
        }

        var loadContext = new AssemblyLoadContext($"behavior-bdd-{Guid.NewGuid():N}", isCollectible: true);
        loadContext.Resolving += static (_, name) =>
        {
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
            }
            catch (Exception)
            {
                return null;
            }
        };
        try
        {
            using var stream = new MemoryStream(assemblyBytes.ToArray());
            var assembly = loadContext.LoadFromStream(stream);
            var installTestsType = assembly.GetExportedTypes()
                .FirstOrDefault(type => typeof(IBehaviorInstallTests).IsAssignableFrom(type) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null);

            if (installTestsType is null)
            {
                return BehaviorInstallTestReport.Fail(
                    "Compiled artifact must export a public parameterless IBehaviorInstallTests implementation.",
                    binding.ScenarioCount);
            }

            var tests = (IBehaviorInstallTests)Activator.CreateInstance(installTestsType)!;
            var context = new ExecutorBehaviorContext(
                new BehaviorExecutionMetadata(
                    Owner: new OwnerId("bdd-gate"),
                    Behavior: envelope.Manifest.Behavior,
                    Revision: new BehaviorRevisionId(artifactHash),
                    Execution: BehaviorExecutionId.New()),
                capabilities,
                time);

            IReadOnlyDictionary<string, string> featureBindings =
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["Behavior"] = envelope.FeatureSource,
                };

            var report = tests.RunAsync(context, featureBindings, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();

            var withResults = BehaviorScenarioBinder.Bind(
                envelope.FeatureSource,
                binding.Scenarios,
                report.Results);
            if (!withResults.Passed)
            {
                return BehaviorInstallTestReport.Fail(withResults.Detail, withResults.ScenarioCount);
            }

            return report.Passed
                ? BehaviorInstallTestReport.FromResults(report.Results, report.Detail)
                : BehaviorInstallTestReport.Fail(report.Detail, binding.ScenarioCount);
        }
        catch (Exception exception)
        {
            return BehaviorInstallTestReport.Fail(exception.Message, binding.ScenarioCount);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
