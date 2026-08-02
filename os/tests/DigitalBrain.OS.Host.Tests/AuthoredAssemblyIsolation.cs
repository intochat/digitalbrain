using System.Net;
using System.Net.Http.Json;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class AuthoredAssemblyIsolation(TestingAppHostFixture fixture)
{
    private const string KnownStateProtectionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string IsolationOwner = "l2-assembly-isolation-owner";

    private const string MarkerProgram =
        """
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record IsolationTrigger(string Label) : Synapse;

        public sealed class IsolationMarkerProgram : IBehaviorProgram<IsolationTrigger>
        {
            public ValueTask ExecuteAsync(
                IsolationTrigger trigger,
                IBehaviorContext context,
                CancellationToken cancellationToken)
            {
                context.SetState("outcome", "isolation:" + trigger.Label);
                return ValueTask.CompletedTask;
            }
        }

        public sealed class IsolationInstallTests : IBehaviorInstallTests
        {
            public ValueTask<BehaviorInstallTestReport> RunAsync(
                IBehaviorContext context,
                IReadOnlyDictionary<string, string> features,
                CancellationToken cancellationToken)
                => ValueTask.FromResult(BehaviorInstallTestReport.FromResults(
                [
                    new BehaviorScenarioResult(
                        "scenario.install-gate-passes",
                        "install gate passes",
                        "bind.install-gate-passes",
                        true,
                        "green"),
                ],
                "green"));
        }
        """;

    private const string MarkerFeature =
        """
        Feature: isolation sample
          Scenario: install gate passes
            Then the install gate passes
        """;

    [Fact(DisplayName =
        "InProcess ExecuteLegacyAsync is closed and never succeeds against authored assembly bytes")]
    public async Task InProcessExecuteLegacyIsClosedAndDoesNotLoadAuthoredBytes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var compile = CompileMarker();
        Assert.False(string.IsNullOrWhiteSpace(ReadAssemblySimpleName(compile.AssemblyBytes)));

        var executor = new InProcessBehaviorExecutor();
        var outcome = await executor.ExecuteLegacyAsync(
            new LegacyBehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId("closed-legacy-owner"),
                    new BehaviorId("com.digitalbrain.isolation"),
                    new BehaviorRevisionId(compile.Digest),
                    BehaviorExecutionId.New()),
                compile.AssemblyBytes,
                compile.Digest,
                "IsolationTrigger",
                """{"Label":"closed"}""",
                new ClosedCapabilityResolver(),
                TimeProvider.System),
            cancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal(BehaviorExecutionCodes.InProcessClosed, outcome.Outcome);
        Assert.DoesNotContain("isolation:", outcome.Outcome, StringComparison.Ordinal);
        Assert.DoesNotContain("IsolationMarkerProgram", outcome.Outcome, StringComparison.Ordinal);
    }

    [Fact(DisplayName =
        "InProcess hardened ExecuteAsync remains closed without executing authored bytes")]
    public async Task InProcessHardenedExecuteAsyncRemainsClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var compile = CompileMarker();
        var executor = new InProcessBehaviorExecutor();
        var outcome = await executor.ExecuteAsync(
            new BehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId("closed-hardened-owner"),
                    new BehaviorId("com.digitalbrain.isolation"),
                    new BehaviorRevisionId(compile.Digest),
                    BehaviorExecutionId.New()),
                compile.AssemblyBytes,
                compile.Digest,
                NeuronId.For<ITask>(new OwnerId("closed-hardened-owner"), "t"),
                new AttemptId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                "IsolationTrigger",
                new ProtectedPayloadReference(Guid.Parse("22222222-2222-2222-2222-222222222222")),
                Capabilities: [],
                DateTimeOffset.UtcNow,
                NeuronId.For<IWorker>(new OwnerId("closed-hardened-owner"), "w")),
            cancellationToken);

        Assert.False(outcome.Succeeded);
        Assert.Equal(BehaviorExecutionCodes.InProcessClosed, outcome.Outcome);
    }

    [Fact(DisplayName =
        "Behavior Host program loader alone loads the pinned artifact and returns a successful unloadable execution")]
    public async Task BehaviorHostProgramLoaderLoadsPinnedArtifactAndUnloads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var compile = CompileMarker();

        var outcome = await BehaviorProgramLoader.ExecuteAsync(
            new LegacyBehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId(IsolationOwner),
                    new BehaviorId("com.digitalbrain.isolation"),
                    new BehaviorRevisionId(compile.Digest),
                    BehaviorExecutionId.New()),
                compile.AssemblyBytes,
                compile.Digest,
                "IsolationTrigger",
                """{"Label":"host-load"}""",
                new ClosedCapabilityResolver(),
                TimeProvider.System),
            cancellationToken);

        Assert.True(outcome.Succeeded, outcome.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, outcome.Outcome);

        // A second host execution of the same bytes must succeed again after the collectible unload.
        var again = await BehaviorProgramLoader.ExecuteAsync(
            new LegacyBehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId(IsolationOwner),
                    new BehaviorId("com.digitalbrain.isolation"),
                    new BehaviorRevisionId(compile.Digest),
                    BehaviorExecutionId.New()),
                compile.AssemblyBytes,
                compile.Digest,
                "IsolationTrigger",
                """{"Label":"host-load-again"}""",
                new ClosedCapabilityResolver(),
                TimeProvider.System),
            cancellationToken);
        Assert.True(again.Succeeded, again.Outcome);
        Assert.Equal(BehaviorExecutionCodes.Succeeded, again.Outcome);
    }

    [Fact(
        Timeout = 300_000,
        DisplayName =
            "Process boundary: TestingAppHost Behavior Host alone accepts pinned deploy/activate; silo residual load path is closed")]
    public async Task SiloUsesHostExecutorAndBehaviorHostAloneAcceptsPinnedArtifact()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);

        var silo = host.Resource(TestingAppHostFixture.SiloResourceName);
        var behaviorHost = host.Resource(TestingAppHostFixture.BehaviorHostResourceName);
        await silo.WaitUntilHealthyAsync(cancellationToken);
        await behaviorHost.WaitUntilHealthyAsync(cancellationToken);

        var sample = await DeployAndActivateSignedSampleAsync(behaviorHost, cancellationToken);
        using var client = behaviorHost.CreateHttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var reDeploy = await client.PostAsJsonAsync(
            "v1/behaviors/deploy",
            new
            {
                owner = IsolationOwner,
                behavior = "com.digitalbrain.isolation",
                artifactHash = sample.Digest.Value,
                artifactBytesBase64 = Convert.ToBase64String(sample.ArtifactBytes),
                assemblyBytesBase64 = Convert.ToBase64String(sample.AssemblyBytes),
                signatureBase64 = Convert.ToBase64String(sample.Signature),
            },
            cancellationToken);
        Assert.True(reDeploy.IsSuccessStatusCode, await reDeploy.Content.ReadAsStringAsync(cancellationToken));

        using var activate = await client.PostAsJsonAsync(
            "v1/behaviors/activate",
            new
            {
                owner = IsolationOwner,
                behavior = "com.digitalbrain.isolation",
                artifactHash = sample.Digest.Value,
            },
            cancellationToken);
        Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync(cancellationToken));

        using var health = await silo.CreateHttpClient().GetAsync(
            new Uri(TestingAppHostFixture.HealthPath, UriKind.Relative),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        var closed = new InProcessBehaviorExecutor();
        var refused = await closed.ExecuteLegacyAsync(
            new LegacyBehaviorExecutionRequest(
                new BehaviorExecutionMetadata(
                    new OwnerId(IsolationOwner),
                    new BehaviorId("com.digitalbrain.isolation"),
                    new BehaviorRevisionId(sample.Digest.Value),
                    BehaviorExecutionId.New()),
                sample.AssemblyBytes,
                sample.Digest.Value,
                sample.TriggerTypeName,
                """{"Label":"silo-refused"}""",
                new ClosedCapabilityResolver(),
                TimeProvider.System),
            cancellationToken);
        Assert.False(refused.Succeeded);
        Assert.Equal(BehaviorExecutionCodes.InProcessClosed, refused.Outcome);
    }

    private static CompiledMarker CompileMarker()
    {
        var compiler = new BehaviorCompiler();
        var compile = compiler.Compile(MarkerProgram, new BehaviorId("com.digitalbrain.isolation"));
        Assert.True(compile.Succeeded, compile.Diagnostics);
        var digest = BehaviorArtifactDigest.Compute(compile.AssemblyBytes.Span).Value;
        return new CompiledMarker(compile.AssemblyBytes.ToArray(), digest);
    }

    private static async Task<SignedSample> DeployAndActivateSignedSampleAsync(
        HostedResource behaviorHost,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Security:StateProtectionKey"] = KnownStateProtectionKey,
            })
            .Build();
        DurablePayloadProtectionHosting.Configure(services, configuration);
        services.AddSingleton<IBehaviorArtifactTrust>(static provider =>
            new BehaviorArtifactTrust(provider.GetRequiredService<IDurablePayloadProtector>()));
        await using var provider = services.BuildServiceProvider();
        var trust = provider.GetRequiredService<IBehaviorArtifactTrust>();

        var behavior = new BehaviorId("com.digitalbrain.isolation");
        var compiler = new BehaviorCompiler();
        var compile = compiler.Compile(MarkerProgram, behavior);
        Assert.True(compile.Succeeded, compile.Diagnostics);
        Assert.NotNull(compile.Contract);
        var signedCase = Assert.Single(compile.Contract.Cases);

        var envelope = BehaviorNeuron.CreateProposalEnvelope(
            behavior,
            "Isolation",
            "Authored assembly isolation sample",
            MarkerProgram,
            MarkerFeature,
            compile.AssemblyBytes,
            compile.CompilerEvidenceJson,
            compile.Contract,
            []);
        var written = CanonicalArtifactWriter.Write(envelope);
        var signature = trust.Sign(written.Digest.Value);

        using var client = behaviorHost.CreateHttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var deploy = await client.PostAsJsonAsync(
            "v1/behaviors/deploy",
            new
            {
                owner = IsolationOwner,
                behavior = behavior.Value,
                artifactHash = written.Digest.Value,
                artifactBytesBase64 = Convert.ToBase64String(written.Bytes),
                assemblyBytesBase64 = Convert.ToBase64String(compile.AssemblyBytes.Span),
                signatureBase64 = Convert.ToBase64String(signature),
            },
            cancellationToken);
        Assert.True(deploy.IsSuccessStatusCode, await deploy.Content.ReadAsStringAsync(cancellationToken));

        using var activate = await client.PostAsJsonAsync(
            "v1/behaviors/activate",
            new
            {
                owner = IsolationOwner,
                behavior = behavior.Value,
                artifactHash = written.Digest.Value,
            },
            cancellationToken);
        Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync(cancellationToken));

        return new SignedSample(
            written.Digest,
            written.Bytes.ToArray(),
            compile.AssemblyBytes.ToArray(),
            signature.ToArray(),
            signedCase.CaseName);
    }

    private static string ReadAssemblySimpleName(ReadOnlyMemory<byte> assemblyBytes)
    {
        using var stream = new MemoryStream(assemblyBytes.ToArray(), writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        return metadata.GetString(definition.Name);
    }

    private sealed record CompiledMarker(byte[] AssemblyBytes, string Digest);

    private sealed record SignedSample(
        BehaviorArtifactDigest Digest,
        byte[] ArtifactBytes,
        byte[] AssemblyBytes,
        byte[] Signature,
        string TriggerTypeName);

    private sealed class ClosedCapabilityResolver : IBehaviorCapabilityResolver
    {
        public TContract Get<TContract>(string name = "default")
            where TContract : class, INeuron
            => throw new InvalidOperationException("Capabilities are not granted in isolation probes.");
    }
}
