using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Broker.Hosting;
using DigitalBrain.Broker.Packages;
using DigitalBrain.Broker.Sandbox;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Microsoft.Extensions.Options;

namespace DigitalBrain.Tests;

public sealed class SandboxFacts
{
    private const string PlainText = "plain-text";

    [Fact]
    public async Task ProcessAppsAreDisabledUntilTheSandboxPenetrationTestCloses()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: false);

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.AppDisabled, result.Denial);
        Assert.Contains("G-8", result.Explanation, StringComparison.Ordinal);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task ASignedProcessAppRunsNetworkDeniedAndWithoutAnOrleansGateway()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var filter = new RecordingCallFilter();
        var meters = new RecordingMeterSink();
        var gateway = Gateway(package, sandbox, enabled: true, filter: filter, meters: meters);

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.True(result.Allowed);
        Assert.True(result.Succeeded);
        Assert.True(sandbox.Invoked);
        Assert.NotNull(sandbox.Spec);
        Assert.Equal(SandboxNetworkPolicy.Denied, sandbox.Spec!.Network);
        Assert.False(sandbox.Spec.OrleansGateway);
        Assert.True(sandbox.Spec.ReadOnlyRootFilesystem);
        Assert.Equal(0.5, sandbox.Spec.Limits.CpuCount);
        Assert.Equal(256L * 1024 * 1024, sandbox.Spec.Limits.MemoryBytes);
        Assert.Equal(16, sandbox.Spec.Limits.ProcessCount);
        Assert.Equal(ProcessAppFixtures.Image, sandbox.Spec.Image);
        Assert.NotNull(filter.Request);
        Assert.Equal(CallerKind.App, filter.Request!.Caller.Kind);
        Assert.Equal(TrustedEdge.AppProxy, filter.Request.Caller.StampedBy);
        Assert.Equal(package.Manifest.Id, filter.Request.Caller.AppId);
        var meter = Assert.Single(meters.Events);
        Assert.Equal(ProcessAppMeters.SandboxRun, meter.MeterId);
        Assert.Equal(package.Manifest.Id, meter.AppId);
    }

    [Fact]
    public async Task AnUnsignedPackageIsRejectedBeforeActivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var signed = ProcessAppFixtures.Signed();
        var package = signed with { Signature = null };
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true, trustedKeys: ProcessAppFixtures.TrustedKeys(signed));

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, result.Denial);
        Assert.Contains("unsigned", result.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task APackageFromAnUntrustedPublisherIsRejectedAtVerification()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true,
            trustedKeys: new Dictionary<string, string>(StringComparer.Ordinal));

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, result.Denial);
        Assert.Contains("not a trusted", result.Explanation, StringComparison.Ordinal);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task ATamperedPackageFailsSignatureVerification()
    {
        var ct = TestContext.Current.CancellationToken;
        var signed = ProcessAppFixtures.Signed();
        var package = signed with { ContentHash = [.. signed.ContentHash] };
        package.ContentHash[0] ^= 0xFF;
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true, trustedKeys: ProcessAppFixtures.TrustedKeys(signed));

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Contains("does not match its content", result.Explanation, StringComparison.Ordinal);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task AnAssemblyReferencingABannedApiIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var assemblies = new[]
        {
            new ProcessAppAssembly { Name = "lib/acme.sample.dll", Bytes = ProcessAppFixtures.TestAssemblyBytes() },
        };
        var package = ProcessAppFixtures.Signed(assemblies: assemblies);
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true, trustedKeys: ProcessAppFixtures.TrustedKeys(package));

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, result.Denial);
        Assert.Contains("banned API", result.Explanation, StringComparison.Ordinal);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public void TheBannedApiAnalyzerFindsABannedReferenceInARealAssembly()
    {
        var findings = new BannedApiAnalyzer().Analyze(ProcessAppFixtures.TestAssemblyBytes());

        Assert.Contains("System.IO.File", findings, StringComparer.Ordinal);
    }

    [Fact]
    public void TheBannedApiAnalyzerFindsNothingWhenNoBannedApiIsReferenced()
    {
        var findings = new BannedApiAnalyzer(["Contoso.NotAReal.Api"])
            .Analyze(ProcessAppFixtures.TestAssemblyBytes());

        Assert.Empty(findings);
    }

    [Fact]
    public async Task ACallThatUsesAnUndeclaredDataClassIsDenied()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true, trustedKeys: ProcessAppFixtures.TrustedKeys(package));

        var result = await gateway.InvokeAsync(Call(package) with { DataClasses = ["person.birthDate"] }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.MissingGrant, result.Denial);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task AProcessCallThatReachesForAHostIsDeniedBecauseNetworkIsOffByDefault()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var gateway = Gateway(package, sandbox, enabled: true, trustedKeys: ProcessAppFixtures.TrustedKeys(package));

        var result = await gateway.InvokeAsync(Call(package) with { EgressHosts = ["evil.example"] }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, result.Denial);
        Assert.Contains("no network", result.Explanation, StringComparison.Ordinal);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public async Task TheCallFilterCanDenyAProcessCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var package = ProcessAppFixtures.Signed();
        var sandbox = new FakeSandbox();
        var filter = new RecordingCallFilter
        {
            Decision = CallDecision.Deny(CallDenial.MissingAllowance, "no allowance"),
        };
        var gateway = Gateway(package, sandbox, enabled: true, filter: filter,
            trustedKeys: ProcessAppFixtures.TrustedKeys(package));

        var result = await gateway.InvokeAsync(Call(package), ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.MissingAllowance, result.Denial);
        Assert.False(sandbox.Invoked);
    }

    [Fact]
    public void ThePackageReaderReadsASignedIntoChatAppPackage()
    {
        var (nupkg, trustedKeys, manifest) = ProcessAppFixtures.BuildNupkg();
        var package = new NupkgProcessAppPackageReader().Read(nupkg);

        Assert.Equal(AppKind.Process, package.Manifest.Kind);
        Assert.Equal(ProcessAppFixtures.Image, package.Sandbox.Image);
        Assert.NotNull(package.Signature);
        var verification = new EcdsaProcessPackageVerifier(trustedKeys).Verify(package);
        Assert.True(verification.Allowed, string.Join("; ", verification.Reasons));

        var drift = new NupkgProcessAppPackageReader().Read(ProcessAppFixtures.BuildNupkg().Nupkg);
        Assert.Equal(manifest.Id, drift.Manifest.Id);
        Assert.Equal(package.ContentHash, drift.ContentHash);
    }

    [Fact]
    public void ThePackageReaderRejectsANonIntoChatAppPackage()
    {
        var (nupkg, _, _) = ProcessAppFixtures.BuildNupkg(packageType: "Widget");

        var error = Assert.Throws<ProcessPackageException>(() => new NupkgProcessAppPackageReader().Read(nupkg));

        Assert.Contains("IntoChatApp", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDockerRuntimeBuildsANetworkDeniedLimitedContainerCommand()
    {
        var spec = new SandboxSpec
        {
            AppId = "acme.sample-process",
            Image = ProcessAppFixtures.Image,
            Command = ["run"],
            Limits = new SandboxLimits { CpuCount = 0.5, MemoryBytes = 128L * 1024 * 1024, ProcessCount = 8 },
        };

        var arguments = DockerSandboxRuntime.BuildRunArguments(spec,
            new SandboxRunRequest { Spec = spec, Operation = "Echo" }).ToList();

        Assert.Contains("--network", arguments);
        Assert.Equal("none", arguments[arguments.IndexOf("--network") + 1]);
        Assert.Contains("--cpus", arguments);
        Assert.Contains("--memory", arguments);
        Assert.Contains("--pids-limit", arguments);
        Assert.Contains("--read-only", arguments);
        Assert.DoesNotContain("ORLEANS", string.Join(' ', arguments), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Timeout = 120_000)]
    public async Task TheDockerRuntimeRunsAContainerWhenDockerIsAvailable()
    {
        var ct = TestContext.Current.CancellationToken;
        var runtime = new DockerSandboxRuntime();
        if (!runtime.IsAvailable)
        {
            Assert.Skip("Docker is not available on this host.");
            return;
        }

        var result = await runtime.RunAsync(new SandboxRunRequest
        {
            Spec = new SandboxSpec
            {
                AppId = "acme.sample-process",
                Image = "alpine:3",
                Command = ["echo", "sandbox-ok"],
            },
            Operation = "Echo",
        }, ct);

        Assert.True(result.Succeeded, result.Failure);
        Assert.Contains("sandbox-ok", result.Output, StringComparison.Ordinal);
    }

    private static ProcessCallRequest Call(ProcessAppPackage package) => new()
    {
        Package = package,
        Caller = new CallerContext
        {
            PrincipalId = "owner",
            AccountId = "acct",
            WorkspaceId = "ws-1",
            Kind = CallerKind.User,
            StampedBy = TrustedEdge.AuthenticatedHttp,
            IntentId = "intent-1",
        },
        Operation = "Echo",
        DataClasses = [PlainText],
        IntentId = "intent-1",
    };

    private static ProcessAppGateway Gateway(
        ProcessAppPackage package,
        ISandboxRuntime sandbox,
        bool enabled,
        RecordingCallFilter? filter = null,
        RecordingMeterSink? meters = null,
        Dictionary<string, string>? trustedKeys = null)
    {
        trustedKeys ??= ProcessAppFixtures.TrustedKeys(package);
        var verifier = new EcdsaProcessPackageVerifier(trustedKeys);
        return new ProcessAppGateway(
            verifier,
            sandbox,
            filter ?? new RecordingCallFilter(),
            meters ?? new RecordingMeterSink(),
            new InMemoryAppObservationStore(),
            Options.Create(new SandboxOptions { Enabled = enabled }));
    }

    private sealed class FakeSandbox : ISandboxRuntime
    {
        public bool IsAvailable => true;

        public bool Invoked { get; private set; }

        public SandboxSpec? Spec { get; private set; }

        public SandboxRunResult Result { get; init; } = new()
        {
            Succeeded = true,
            Output = "ok",
            UsedDataClasses = [PlainText],
            UsedMeters = ["sample.call"],
        };

        public ValueTask<SandboxRunResult> RunAsync(
            SandboxRunRequest request, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            Spec = request.Spec;
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class RecordingCallFilter : ICallFilter
    {
        public CallRequest? Request { get; private set; }

        public CallDecision Decision { get; init; } = CallDecision.Allow();

        public ValueTask<CallDecision> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult(Decision);
        }
    }

    private sealed class RecordingMeterSink : IMeterSink
    {
        private readonly List<MeterEvent> events = [];

        public IReadOnlyList<MeterEvent> Events => events;

        public ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default)
        {
            events.Add(meterEvent);
            return ValueTask.CompletedTask;
        }
    }
}