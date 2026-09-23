using DigitalBrain.Apps;
using DigitalBrain.Broker;
using DigitalBrain.Compute;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class GatewayFacts
{
    private const string SearchQuery = "search.query";

    [Fact]
    public async Task AThirdPartyCallTravelsThroughTheBrokerAndRecordsAMeter()
    {
        var ct = TestContext.Current.CancellationToken;
        var filter = new RecordingCallFilter();
        var meters = new RecordingMeterSink();
        var transport = new FakeRemoteApp();
        var store = new InMemoryAppObservationStore();
        var gateway = Gateway(filter, transport, store, meters);
        var manifest = RemoteManifest(SearchQuery);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = manifest,
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
            EgressHosts = ["api.acme.example"],
            IntentId = "intent-1",
        }, ct);

        Assert.True(result.Allowed);
        Assert.True(transport.Invoked);
        Assert.NotNull(filter.Request);
        Assert.Equal(CallerKind.App, filter.Request.Caller.Kind);
        Assert.Equal(TrustedEdge.AppProxy, filter.Request.Caller.StampedBy);
        Assert.Equal(manifest.Id, filter.Request.Caller.AppId);
        var meter = Assert.Single(meters.Events);
        Assert.Equal(BrokerMeters.RemoteCall, meter.MeterId);
        Assert.Equal(manifest.Id, meter.AppId);
        Assert.Equal(MeterSource.CallFilter, meter.Source);
    }

    [Fact]
    public async Task DeclaredVersusObservedDiffIsZeroForAnHonestRemoteApp()
    {
        var ct = TestContext.Current.CancellationToken;
        var manifest = RemoteManifest(SearchQuery);
        var transport = new FakeRemoteApp
        {
            UsedDataClasses = [SearchQuery],
            UsedMeters = ["search.request"],
        };
        var store = new InMemoryAppObservationStore();
        var gateway = Gateway(new RecordingCallFilter(), transport, store);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = manifest,
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
            EgressHosts = ["api.acme.example"],
        }, ct);

        Assert.True(result.Allowed);
        Assert.NotNull(result.Diff);
        Assert.True(result.Diff.IsClean);
        Assert.Empty(result.Diff.UndeclaredDataClasses);
        Assert.Empty(result.Diff.UndeclaredMeters);
        Assert.Empty(result.Diff.UnusedPermissions);
        Assert.True(store.Diff(manifest).IsClean);
    }

    [Fact]
    public async Task ADataClassTheManifestDoesNotDeclareIsDeniedBeforeTheEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new FakeRemoteApp();
        var gateway = Gateway(new RecordingCallFilter(), transport);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = RemoteManifest(SearchQuery),
            Caller = Caller(),
            Operation = "search",
            DataClasses = ["person.birthDate"],
        }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.MissingGrant, result.Denial);
        Assert.False(transport.Invoked);
    }

    [Fact]
    public async Task EgressOutsideTheDataClassRuleIsDeniedBeforeTheEndpoint()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new FakeRemoteApp();
        var gateway = Gateway(new RecordingCallFilter(), transport);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = RemoteManifest(SearchQuery),
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
            EgressHosts = ["evil.example"],
        }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.UntrustedCaller, result.Denial);
        Assert.False(transport.Invoked);
    }

    [Fact]
    public async Task TheCallFilterCanDenyTheCall()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new FakeRemoteApp();
        var filter = new RecordingCallFilter
        {
            Decision = CallDecision.Deny(CallDenial.MissingGrant, "no grant"),
        };
        var gateway = Gateway(filter, transport);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = RemoteManifest(SearchQuery),
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
        }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.MissingGrant, result.Denial);
        Assert.False(transport.Invoked);
    }

    [Fact]
    public async Task TheBrokerServesOnlyRemoteApps()
    {
        var ct = TestContext.Current.CancellationToken;
        var transport = new FakeRemoteApp();
        var gateway = Gateway(new RecordingCallFilter(), transport);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = RemoteManifest(SearchQuery) with { Kind = AppKind.Declarative },
            Caller = Caller(),
            Operation = "search",
        }, ct);

        Assert.False(result.Allowed);
        Assert.Equal(CallDenial.AppDisabled, result.Denial);
        Assert.False(transport.Invoked);
    }

    [Fact]
    public async Task UndeclaredUseSurfacesInTheDeclaredVersusObservedDiff()
    {
        var ct = TestContext.Current.CancellationToken;
        var manifest = RemoteManifest(SearchQuery);
        var transport = new FakeRemoteApp
        {
            UsedDataClasses = [SearchQuery, "person.email"],
            UsedMeters = ["search.request", "secret.meter"],
        };
        var store = new InMemoryAppObservationStore();
        var gateway = Gateway(new RecordingCallFilter(), transport, store);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = manifest,
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
            EgressHosts = ["api.acme.example"],
        }, ct);

        Assert.NotNull(result.Diff);
        Assert.False(result.Diff.IsClean);
        Assert.Equal(["person.email"], result.Diff.UndeclaredDataClasses);
        Assert.Equal(["secret.meter"], result.Diff.UndeclaredMeters);
    }

    [Fact]
    public async Task TheBrokerRunsInsideTheSingleCallFilter()
    {
        var ct = TestContext.Current.CancellationToken;
        var stage = new RecordingStage();
        var transport = new FakeRemoteApp();
        var gateway = new BrokerGateway(
            new CallFilter([stage]), new RecordingMeterSink(), EgressPolicy(), new InMemoryAppObservationStore(), transport);

        var result = await gateway.InvokeAsync(new RemoteCallRequest
        {
            Manifest = RemoteManifest(SearchQuery),
            Caller = Caller(),
            Operation = "search",
            DataClasses = [SearchQuery],
            EgressHosts = ["api.acme.example"],
        }, ct);

        Assert.True(result.Allowed);
        Assert.NotNull(stage.Request);
        Assert.Equal(CallerKind.App, stage.Request.Caller.Kind);
        Assert.Equal(TrustedEdge.AppProxy, stage.Request.Caller.StampedBy);
    }

    private static BrokerGateway Gateway(
        ICallFilter filter,
        IRemoteAppTransport transport,
        IAppObservationStore? store = null,
        RecordingMeterSink? meters = null)
        => new(filter, meters ?? new RecordingMeterSink(), EgressPolicy(), store ?? new InMemoryAppObservationStore(), transport);

    private static IEgressPolicy EgressPolicy() => new DataClassEgressPolicy(
        [new EgressRule { DataClass = SearchQuery, AllowedHosts = ["*.acme.example"] }]);

    private static AppManifest RemoteManifest(params string[] dataClasses) => new()
    {
        Id = "com.acme.search",
        Version = "1.0.0",
        Publisher = "acme",
        Kind = AppKind.Remote,
        Name = "Acme Search",
        DescriptionForPeople = "Search a company catalog.",
        DescriptionForModel = "Searches the Acme catalog.",
        RemoteEndpoint = "https://acme.example/mcp",
        Permissions = [.. dataClasses.Select(dataClass => new AppPermission
        {
            SemanticTypeId = dataClass,
            Reason = "The operation reads this data class.",
        })],
        Meters = [new AppMeter { MeterId = "search.request", Unit = "request", Aggregation = "sum" }],
    };

    private static CallerContext Caller() => new()
    {
        PrincipalId = "owner",
        AccountId = "acct",
        WorkspaceId = "ws-1",
        Kind = CallerKind.User,
        StampedBy = TrustedEdge.AuthenticatedHttp,
        IntentId = "intent-1",
    };

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

    private sealed class RecordingStage : ICallFilterStage
    {
        public CallRequest? Request { get; private set; }

        public ValueTask<CallDecision?> EvaluateAsync(CallRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return ValueTask.FromResult<CallDecision?>(null);
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

    private sealed class FakeRemoteApp : IRemoteAppTransport
    {
        public bool Invoked { get; private set; }

        public IReadOnlyList<string> UsedDataClasses { get; init; } = [];

        public IReadOnlyList<string> UsedMeters { get; init; } = [];

        public ValueTask<RemoteAppResponse> InvokeAsync(
            AppManifest manifest, RemoteCallRequest request, CancellationToken cancellationToken = default)
        {
            Invoked = true;
            return ValueTask.FromResult(new RemoteAppResponse
            {
                Succeeded = true,
                Output = "ok",
                UsedDataClasses = UsedDataClasses,
                UsedMeters = UsedMeters,
            });
        }
    }
}
