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

        if (envelope.Features.Count == 0
            || envelope.Features.Values.All(string.IsNullOrWhiteSpace)
            || !envelope.Features.Values.Any(feature => feature.Contains("Scenario:", StringComparison.Ordinal)))
        {
            return BehaviorInstallTestReport.Fail("A behavior proposal must include at least one Gherkin Scenario.");
        }

        var scenarioCount = envelope.Features.Values
            .SelectMany(feature => feature.Split('\n'))
            .Count(line => line.TrimStart().StartsWith("Scenario:", StringComparison.Ordinal));

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
                    scenarioCount);
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

            return tests.RunAsync(context, envelope.Features, CancellationToken.None)
                .AsTask()
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            return BehaviorInstallTestReport.Fail(exception.Message, scenarioCount);
        }
        finally
        {
            loadContext.Unload();
        }
    }
}
