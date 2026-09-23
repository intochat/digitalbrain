using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Broker.Packages;
using DigitalBrain.Broker.Sandbox;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Tests;

// A certification run of the sample `process` app: read and verify the signed package, then run
// every declared operation through the gateway on a fake sandbox and assert a clean declared-vs-
// observed diff with network denied and no Orleans gateway.
public sealed class ProcessAppCertificationFacts
{
    [Fact]
    public async Task ASampleProcessAppCertifiesOnFakesWithNoNetworkAndACleanDiff()
    {
        var ct = TestContext.Current.CancellationToken;
        var (nupkg, trustedKeys, manifest) = ProcessAppFixtures.BuildNupkg();
        var package = new NupkgProcessAppPackageReader().Read(nupkg);
        var verifier = new EcdsaProcessPackageVerifier(trustedKeys);

        var verification = verifier.Verify(package);
        Assert.True(verification.Allowed, string.Join("; ", verification.Reasons));

        var sandbox = new RecordingSandbox();
        var meters = new RecordingMeterSink();
        var observations = new InMemoryAppObservationStore();
        var gateway = new ProcessAppGateway(
            verifier, sandbox, new AllowingFilter(), meters, observations,
            Options.Create(new SandboxOptions { Enabled = true }));

        var dataClasses = manifest.Permissions.Select(permission => permission.SemanticTypeId).ToArray();
        foreach (var operation in manifest.Operations)
        {
            var result = await gateway.InvokeAsync(new ProcessCallRequest
            {
                Package = package,
                Caller = new CallerContext
                {
                    PrincipalId = "owner",
                    AccountId = "account",
                    WorkspaceId = "workspace-a",
                    Kind = CallerKind.User,
                    StampedBy = TrustedEdge.AuthenticatedHttp,
                },
                Operation = operation.Name,
                DataClasses = dataClasses,
            }, ct);

            Assert.True(result.Allowed, result.Explanation);
            Assert.True(result.Succeeded, result.Explanation);
            Assert.NotNull(result.Diff);
            Assert.True(result.Diff!.IsClean,
                $"Undeclared: {string.Join(", ", result.Diff.UndeclaredDataClasses)} / {string.Join(", ", result.Diff.UndeclaredMeters)}");
        }

        Assert.Equal(manifest.Operations.Count, meters.Events.Count);
        Assert.Equal(manifest.Operations.Count, sandbox.Specs.Count);
        Assert.All(sandbox.Specs, spec =>
        {
            Assert.Equal(SandboxNetworkPolicy.Denied, spec.Network);
            Assert.False(spec.OrleansGateway);
            Assert.True(spec.ReadOnlyRootFilesystem);
        });
    }

    private sealed class RecordingSandbox : ISandboxRuntime
    {
        public List<SandboxSpec> Specs { get; } = [];

        public bool IsAvailable => true;

        public ValueTask<SandboxRunResult> RunAsync(
            SandboxRunRequest request, CancellationToken cancellationToken = default)
        {
            Specs.Add(request.Spec);
            return ValueTask.FromResult(new SandboxRunResult
            {
                Succeeded = true,
                Output = "echo",
                UsedDataClasses = ["plain-text"],
                UsedMeters = ["sample.call"],
            });
        }
    }

    private sealed class AllowingFilter : ICallFilter
    {
        public ValueTask<CallDecision> AuthorizeAsync(
            CallRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CallDecision.Allow());
    }

    private sealed class RecordingMeterSink : IMeterSink
    {
        public List<MeterEvent> Events { get; } = [];

        public ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(meterEvent);
            return ValueTask.CompletedTask;
        }
    }
}