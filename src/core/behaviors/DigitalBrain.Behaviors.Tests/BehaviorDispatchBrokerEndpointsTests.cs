using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorDispatchBrokerEndpointsTests
{
    private static readonly OwnerId BoundOwner = new("dispatch-broker-owner");
    private static readonly NeuronId BoundTask = NeuronId.For<ITask>(BoundOwner, "dispatch-broker-task");
    private static readonly AttemptId BoundAttempt = new(Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    private static readonly NeuronId CapabilityTarget = new("behaviors.dispatch-probe", BoundOwner, "probe");
    [Fact(DisplayName = "an emit request is its own command identity, so a retried request cannot emit twice")]
    public void EmitCommandIdIsDerivedFromTheRequest()
    {
        var alias = "behaviors.probe-fact-raised";
        var factJson = """{"label":"once"}""";
        var retried = GrainBehaviorCapabilityDispatchAccess.EmitCommandId(
            BoundTask,
            BoundAttempt,
            alias,
            factJson);

        Assert.Equal(
            retried,
            GrainBehaviorCapabilityDispatchAccess.EmitCommandId(BoundTask, BoundAttempt, alias, factJson));
        Assert.NotEqual(
            retried,
            GrainBehaviorCapabilityDispatchAccess.EmitCommandId(BoundTask, BoundAttempt, alias, """{"label":"twice"}"""));
        Assert.NotEqual(
            retried,
            GrainBehaviorCapabilityDispatchAccess.EmitCommandId(
                BoundTask,
                new AttemptId(Guid.Parse("cccccccccccccccccccccccccccccccc")),
                alias,
                factJson));
    }

    private static readonly ProtectedPayloadReference RequestRef = new(
        Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
        new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));
    private static readonly ProtectedPayloadReference ResponseRef = new(
        Guid.Parse("cccccccccccccccccccccccccccccccc"),
        null);
    private const string ValidCredential = "unit-test-dispatch-broker-credential";

    [Fact(DisplayName = "dispatch happy path returns only an opaque response reference and preserves exact edge fidelity")]
    public async Task HappyPathReturnsOnlyOpaqueResponseReferenceWithExactEdge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        using var response = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            ValidDispatchBody(),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(ResponseRef.Id.ToString("N"), body.Id);
        Assert.Null(body.ExpiresAt);
        Assert.DoesNotContain("secret", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, access.DispatchCalls);
        Assert.Equal(BoundOwner, access.LastOwner);
        Assert.Equal(BoundTask, access.LastTask);
        Assert.Equal(BoundAttempt, access.LastAttempt);
        Assert.NotNull(access.LastEdge);
        Assert.Equal(CapabilityTarget, access.LastEdge!.Target);
        Assert.Equal("behaviors.dispatch-probe-request", access.LastEdge.RequestSynapseId);
        Assert.Equal(1, access.LastEdge.RequestSchemaVersion);
        Assert.Equal("behaviors.dispatch-probe-response", access.LastEdge.ResponseSynapseId);
        Assert.Equal(1, access.LastEdge.ResponseSchemaVersion);
        Assert.Equal(RequestRef, access.LastRequestPayload);
    }

    [Fact(DisplayName = "missing, blank, wrong, multi-value, and unconfigured credentials fail closed with zero dispatch access")]
    public async Task CredentialFailuresFailClosedWithoutDispatchAccess()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();
        var body = ValidDispatchBody();

        using var missing = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/dispatch",
            body,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("unauthorized", (await missing.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var blankRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/dispatch")
        {
            Content = JsonContent.Create(body),
        };
        blankRequest.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, "   ");
        using var blank = await client.SendAsync(blankRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, blank.StatusCode);

        using var wrong = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            body,
            cancellationToken,
            credential: "wrong-credential");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var multiRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/dispatch")
        {
            Content = JsonContent.Create(body),
        };
        multiRequest.Headers.TryAddWithoutValidation(
            BehaviorBrokerContract.CredentialHeaderName,
            [ValidCredential, "smuggled"]);
        using var multi = await client.SendAsync(multiRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, multi.StatusCode);

        await using var unconfigured = await StartHostAsync(access, cancellationToken, credential: null);
        using var unconfiguredClient = unconfigured.CreateClient();
        using var unconfiguredResponse = await PostAuthorizedAsync(
            unconfiguredClient,
            "/v1/behaviors/broker/dispatch",
            body,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unconfiguredResponse.StatusCode);

        Assert.Equal(0, access.DispatchCalls);
    }

    [Fact(DisplayName = "endpoint parse layer refuses foreign owner, missing owner, and invalid edge shapes before access delivery")]
    public async Task IdentityAndEdgeRefusalsAreStable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        using var foreign = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = "foreign-owner",
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                edge = ValidEdgeBody(),
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("owner-task-mismatch", (await foreign.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var missingOwner = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            new
            {
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                edge = ValidEdgeBody(),
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingOwner.StatusCode);
        Assert.Equal("missing-owner", (await missingOwner.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var badEdge = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                edge = new
                {
                    targetType = CapabilityTarget.Type,
                    targetOwner = CapabilityTarget.Owner.Value,
                    targetName = CapabilityTarget.Name,
                    requestId = "behaviors.dispatch-probe-request",
                    requestVersion = 0,
                    responseId = "behaviors.dispatch-probe-response",
                    responseVersion = 1,
                },
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badEdge.StatusCode);
        Assert.Equal("invalid-operation-edge", (await badEdge.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var blankIds = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                edge = new
                {
                    targetType = " ",
                    targetOwner = CapabilityTarget.Owner.Value,
                    targetName = CapabilityTarget.Name,
                    requestId = "behaviors.dispatch-probe-request",
                    requestVersion = 1,
                    responseId = "behaviors.dispatch-probe-response",
                    responseVersion = 1,
                },
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, blankIds.StatusCode);
        Assert.Equal("invalid-operation-edge", (await blankIds.Content.ReadAsStringAsync(cancellationToken)).Trim());

        Assert.Equal(0, access.DispatchCalls);
    }

    [Fact(DisplayName = "dispatch handler propagates cancellation without inventing CancellationToken.None")]
    public async Task HandlersPropagateCancellation()
    {
        var live = TestContext.Current.CancellationToken;
        await using var host = await StartHostAsync(new RecordingAccess(), live);
        using var client = host.CreateClient();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PostAuthorizedAsync(
                client,
                "/v1/behaviors/broker/dispatch",
                ValidDispatchBody(),
                cancelled.Token));
    }

    [Fact(DisplayName = "endpoint maps access InvalidOperationException message to stable text/plain BadRequest")]
    public async Task AccessCatalogRefusalsMapToStableReasons()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess
        {
            ThrowOnDispatch = new InvalidOperationException("unknown-target-neuron"),
        };
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        using var response = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/dispatch",
            ValidDispatchBody(),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("unknown-target-neuron", (await response.Content.ReadAsStringAsync(cancellationToken)).Trim());
        Assert.Equal(1, access.DispatchCalls);
    }

    private static object ValidDispatchBody()
        => new
        {
            owner = BoundOwner.Value,
            taskType = BoundTask.Type,
            taskOwner = BoundTask.Owner.Value,
            taskName = BoundTask.Name,
            attempt = BoundAttempt.Value.ToString("N"),
            edge = ValidEdgeBody(),
            requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
        };

    private static object ValidEdgeBody()
        => new
        {
            targetType = CapabilityTarget.Type,
            targetOwner = CapabilityTarget.Owner.Value,
            targetName = CapabilityTarget.Name,
            requestId = "behaviors.dispatch-probe-request",
            requestVersion = 1,
            responseId = "behaviors.dispatch-probe-response",
            responseVersion = 1,
        };

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken,
        string? credential = ValidCredential)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        if (credential is not null)
        {
            request.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, credential);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<RunningHost> StartHostAsync(
        IBehaviorCapabilityDispatchAccess access,
        CancellationToken cancellationToken,
        string? credential = ValidCredential)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BehaviorBrokerContract.CredentialConfigurationKey] = credential,
            })
            .Build();

        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorDispatchBrokerEndpointsTests).Assembly.FullName,
            EnvironmentName = Environments.Development,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration, builder.Environment);
        builder.Services.AddSingleton(access);
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
        app.MapGet("/health", () => Results.Text("Healthy"));
        app.MapBehaviorDispatchBroker();
        await app.StartAsync(cancellationToken);
        return new RunningHost(app, new Uri($"http://127.0.0.1:{port}"));

        static int GetFreeTcpPort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }

    private sealed class RunningHost(WebApplication app, Uri baseAddress) : IAsyncDisposable
    {
        public Uri BaseAddress { get; } = baseAddress;

        public HttpClient CreateClient() => new() { BaseAddress = BaseAddress };

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private sealed class RecordingAccess : IBehaviorCapabilityDispatchAccess
    {
    public BehaviorId EmittedBehavior { get; private set; }

    public string? EmittedAlias { get; private set; }

    public string EmitOutcome { get; set; } = BehaviorFactEmission.Emitted;

    public ValueTask<string> EmitFactAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        BehaviorId behavior,
        string emitAlias,
        string factJson,
        CancellationToken cancellationToken)
    {
        EmittedBehavior = behavior;
        EmittedAlias = emitAlias;
        return ValueTask.FromResult(EmitOutcome);
    }

        public int DispatchCalls { get; private set; }
        public OwnerId LastOwner { get; private set; }
        public NeuronId LastTask { get; private set; }
        public AttemptId LastAttempt { get; private set; }
        public BehaviorCapabilityEdge? LastEdge { get; private set; }
        public ProtectedPayloadReference LastRequestPayload { get; private set; }
        public Exception? ThrowOnDispatch { get; set; }

        public ValueTask<ProtectedPayloadReference> DispatchAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            BehaviorCapabilityEdge edge,
            ProtectedPayloadReference requestPayload,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DispatchCalls++;
            LastOwner = owner;
            LastTask = task;
            LastAttempt = attempt;
            LastEdge = edge;
            LastRequestPayload = requestPayload;
            if (ThrowOnDispatch is not null)
            {
                throw ThrowOnDispatch;
            }

            return ValueTask.FromResult(ResponseRef);
        }
    }

    private sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);
}
