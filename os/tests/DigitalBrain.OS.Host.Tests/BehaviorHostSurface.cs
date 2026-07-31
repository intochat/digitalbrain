using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.HostTests;

public sealed class BehaviorHostSurface(TestingAppHostFixture fixture)
{
    private const string KnownStateProtectionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string SurfaceOwner = "l2-surface-owner";
    private const string SurfaceTaskName = "l2-surface-task";

    private const string GreenProgram =
        """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using DigitalBrain.Abstractions;
        using DigitalBrain.Behaviors;

        public sealed record SampleTrigger(string Label) : Synapse;

        public sealed class SampleProgram : IBehaviorProgram<SampleTrigger>
        {
            public ValueTask ExecuteAsync(SampleTrigger trigger, IBehaviorContext context, CancellationToken cancellationToken)
            {
                context.SetState("outcome", "l2-host:" + trigger.Label);
                return ValueTask.CompletedTask;
            }
        }

        public static class BehaviorEntry
        {
            public static Task RunAsync(BehaviorBrain<SampleTrigger> brain)
                => Task.CompletedTask;
        }

        public sealed class SampleInstallTests : IBehaviorInstallTests
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

    private const string GreenFeature =
        """
        Feature: sample behavior
          Scenario: install gate passes
            Then the install gate passes
        """;

    [Fact(
        Timeout = 300_000,
        DisplayName =
            "TestingAppHost boots BehaviorHost Healthy and executes one signed approved revision end-to-end")]
    public async Task BehaviorHostIsHealthyAndExecutesSignedRevision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);

        var silo = host.Resource(TestingAppHostFixture.SiloResourceName);
        var behaviorHost = host.Resource(TestingAppHostFixture.BehaviorHostResourceName);
        await silo.WaitUntilHealthyAsync(cancellationToken);
        await behaviorHost.WaitUntilHealthyAsync(cancellationToken);

        using var healthClient = behaviorHost.CreateHttpClient();
        healthClient.Timeout = TimeSpan.FromMinutes(5);
        using var health = await healthClient.GetAsync(
            new Uri(TestingAppHostFixture.HealthPath, UriKind.Relative),
            cancellationToken);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);

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

        var compiler = new ContractOnlyBehaviorCompiler();
        var compile = compiler.Compile(GreenProgram, new BehaviorId("com.digitalbrain.sample"));
        Assert.True(compile.Succeeded, compile.Diagnostics);

        var envelope = new BehaviorArtifactEnvelope(
            new BehaviorDefinitionManifest(
                new BehaviorId("com.digitalbrain.sample"),
                "Sample",
                "Sample",
                new BehaviorEntryPoints(
                    [],
                    new BehaviorContractManifest(
                        "com.digitalbrain.sample",
                        1,
                        """{"oneOf":[]}""",
                        [],
                        """{"type":"object"}""")),
                [],
                "Sample host surface program",
                new BehaviorCompilerPolicy("11.0.100-preview.6", "5.6.0", "Preview", "contract-only-v1"),
                [],
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            GreenProgram,
            GreenFeature,
            """{"version":1,"libraries":{}}""",
            compile.AssemblyBytes,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            compile.CompilerEvidenceJson,
            """{"result":"approved","policy":"v1"}""",
            """{"scenarios":1,"passed":true}""");
        var written = CanonicalArtifactWriter.Write(envelope);
        var signature = trust.Sign(written.Digest.Value);

        using var client = behaviorHost.CreateHttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var deploy = await client.PostAsJsonAsync(
            "v1/behaviors/deploy",
            new
            {
                owner = SurfaceOwner,
                behavior = "com.digitalbrain.sample",
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
                owner = SurfaceOwner,
                behavior = "com.digitalbrain.sample",
                artifactHash = written.Digest.Value,
            },
            cancellationToken);
        Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync(cancellationToken));

        var owner = new OwnerId(SurfaceOwner);
        var task = NeuronId.For<ITask>(owner, SurfaceTaskName);
        var attempt = new AttemptId(Guid.NewGuid());
        var execution = BehaviorExecutionId.New();
        var triggerBytes = Encoding.UTF8.GetBytes("""{"Label":"l2"}""");

        using var store = await client.PostAsJsonAsync(
            "v1/behaviors/broker/payloads/store",
            new
            {
                owner = SurfaceOwner,
                taskType = task.Type,
                taskOwner = SurfaceOwner,
                taskName = SurfaceTaskName,
                attempt = attempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(triggerBytes),
            },
            cancellationToken);
        Assert.True(store.IsSuccessStatusCode, await store.Content.ReadAsStringAsync(cancellationToken));
        var stored = await store.Content.ReadFromJsonAsync<StoredPayloadResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(stored);
        Assert.False(string.IsNullOrWhiteSpace(stored.Id));

        using var execute = await client.PostAsJsonAsync(
            "v1/behaviors/execute",
            new
            {
                owner = SurfaceOwner,
                behavior = "com.digitalbrain.sample",
                revision = written.Digest.Value,
                execution = execution.Value.ToString("N"),
                artifactHash = written.Digest.Value,
                triggerTypeName = "SampleTrigger",
                taskType = task.Type,
                taskOwner = SurfaceOwner,
                taskName = SurfaceTaskName,
                attempt = attempt.Value.ToString("N"),
                triggerPayloadId = stored.Id,
                triggerPayloadExpiresAt = stored.ExpiresAt,
                capabilities = Array.Empty<object>(),
                utcNow = DateTimeOffset.UtcNow,
            },
            cancellationToken);
        Assert.True(execute.IsSuccessStatusCode, await execute.Content.ReadAsStringAsync(cancellationToken));
        var outcome = await execute.Content.ReadFromJsonAsync<ExecuteResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(outcome);
        Assert.True(outcome.Succeeded, outcome.Outcome);
        Assert.Equal("executed", outcome.Outcome);
    }

    private sealed record StoredPayloadResponse(string Id, DateTimeOffset? ExpiresAt);

    private sealed record ExecuteResponse(bool Succeeded, string Outcome);
}
