using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorTaskOperationBrokerEndpointsTests
{
    private static readonly OwnerId BoundOwner = new("ops-broker-owner");
    private static readonly NeuronId BoundTask = NeuronId.For<ITask>(BoundOwner, "ops-broker-task");
    private static readonly AttemptId BoundAttempt = new(Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    private static readonly NeuronId CapabilityTarget = NeuronId.For<ITask>(BoundOwner, "capability-target");
    private static readonly ProtectedPayloadReference RequestRef = new(
        Guid.Parse("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
        new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));
    private const string ValidCredential = "unit-test-ops-broker-credential";

    [Fact(DisplayName = "missing, blank, wrong, multi-value, and unconfigured credentials fail closed with zero access calls")]
    public async Task CredentialFailuresFailClosedWithoutAccessCalls()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();
        var body = ValidPrepareBody();

        using var missing = await client.PostAsJsonAsync(
            "/v1/behaviors/broker/operations/prepare",
            body,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Equal("unauthorized", (await missing.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var blankRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/operations/prepare")
        {
            Content = JsonContent.Create(body),
        };
        blankRequest.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, "   ");
        using var blank = await client.SendAsync(blankRequest, cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, blank.StatusCode);

        using var wrong = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            body,
            cancellationToken,
            credential: "wrong-credential");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var multiRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/behaviors/broker/operations/prepare")
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
            "/v1/behaviors/broker/operations/prepare",
            body,
            cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, unconfiguredResponse.StatusCode);

        Assert.Equal(0, access.PrepareCalls);
        Assert.Equal(0, access.ReadCalls);
        Assert.Equal(0, access.TransitionCalls);
    }

    [Fact(DisplayName = "broker credential middleware leaves unrelated health endpoints open")]
    public async Task NonBrokerHealthEndpointRemainsOpen()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        using var health = await client.GetAsync(new Uri("/health", UriKind.Relative), cancellationToken);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", (await health.Content.ReadAsStringAsync(cancellationToken)).Trim());
        Assert.Equal(0, access.PrepareCalls);
    }

    [Fact(DisplayName = "prepare/read/transition preserve DTO fidelity and map stable invalid-request reasons")]
    public async Task PrepareReadTransitionDtoFidelityAndStableValidation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var access = new RecordingAccess();
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        using var prepare = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            ValidPrepareBody(),
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        var prepared = await prepare.Content.ReadFromJsonAsync<SnapshotResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(prepared);
        Assert.Equal(BoundAttempt.Value.ToString("N"), prepared.Attempt);
        Assert.Equal(0, prepared.Sequence);
        Assert.Equal((int)TaskOperationPhase.Prepared, prepared.Phase);
        Assert.Equal(RequestRef.Id.ToString("N"), prepared.RequestPayload!.Id);
        Assert.Equal(1, access.PrepareCalls);

        using var read = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/read",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                sequence = 0,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<ReadResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(readBody);
        Assert.NotNull(readBody.Operation);
        Assert.Equal(1, access.ReadCalls);

        var responseRef = new ProtectedPayloadReference(
            Guid.Parse("cccccccccccccccccccccccccccccccc"),
            null);
        using var transition = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/transition",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                sequence = 0,
                expectedPhase = (int)TaskOperationPhase.Prepared,
                phase = (int)TaskOperationPhase.Completed,
                responsePayload = new { id = responseRef.Id.ToString("N"), expiresAt = (DateTimeOffset?)null },
                redactedSummary = "done",
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, transition.StatusCode);
        var transitioned = await transition.Content.ReadFromJsonAsync<SnapshotResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(transitioned);
        Assert.Equal((int)TaskOperationPhase.Completed, transitioned.Phase);
        Assert.Equal(responseRef.Id.ToString("N"), transitioned.ResponsePayload!.Id);
        Assert.Equal("done", transitioned.RedactedSummary);
        Assert.Equal(1, access.TransitionCalls);

        using var mismatch = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            new
            {
                owner = BoundOwner.Value,
                taskType = BoundTask.Type,
                taskOwner = "foreign-owner",
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                sequence = 0,
                edge = ValidEdgeBody(),
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal("owner-task-mismatch", (await mismatch.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var missingOwner = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            new
            {
                taskType = BoundTask.Type,
                taskOwner = BoundTask.Owner.Value,
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                sequence = 0,
                edge = ValidEdgeBody(),
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, missingOwner.StatusCode);
        Assert.Equal("missing-owner", (await missingOwner.Content.ReadAsStringAsync(cancellationToken)).Trim());

        using var badOwner = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            new
            {
                owner = "bad/owner",
                taskType = BoundTask.Type,
                taskOwner = "bad/owner",
                taskName = BoundTask.Name,
                attempt = BoundAttempt.Value.ToString("N"),
                sequence = 0,
                edge = ValidEdgeBody(),
                requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badOwner.StatusCode);
        var badBody = (await badOwner.Content.ReadAsStringAsync(cancellationToken)).Trim();
        Assert.Equal("invalid-request", badBody);
        Assert.DoesNotContain("ArgumentException", badBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Identity parts", badBody, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "operation handlers propagate cancellation without inventing CancellationToken.None")]
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
                "/v1/behaviors/broker/operations/prepare",
                ValidPrepareBody(),
                cancelled.Token));
    }

    private static object ValidPrepareBody()
        => new
        {
            owner = BoundOwner.Value,
            taskType = BoundTask.Type,
            taskOwner = BoundTask.Owner.Value,
            taskName = BoundTask.Name,
            attempt = BoundAttempt.Value.ToString("N"),
            sequence = 0,
            edge = ValidEdgeBody(),
            requestPayload = new { id = RequestRef.Id.ToString("N"), expiresAt = RequestRef.ExpiresAt },
        };

    private static object ValidEdgeBody()
        => new
        {
            targetType = CapabilityTarget.Type,
            targetOwner = CapabilityTarget.Owner.Value,
            targetName = CapabilityTarget.Name,
            requestId = "request-synapse",
            requestVersion = 1,
            responseId = "response-synapse",
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
        IBehaviorTaskOperationAccess access,
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
            ApplicationName = typeof(BehaviorTaskOperationBrokerEndpointsTests).Assembly.FullName,
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
        app.MapBehaviorTaskOperationBroker();
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

    private sealed class RecordingAccess : IBehaviorTaskOperationAccess
    {
        public int PrepareCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public int TransitionCalls { get; private set; }

        public ValueTask<TaskOperationSnapshot> PrepareAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            TaskOperationEdge edge,
            ProtectedPayloadReference requestPayload,
            CancellationToken cancellationToken)
        {
            PrepareCalls++;
            return ValueTask.FromResult(new TaskOperationSnapshot(
                attempt,
                sequence,
                edge,
                requestPayload,
                TaskOperationPhase.Prepared,
                ResponsePayload: null,
                RedactedSummary: null));
        }

        public ValueTask<ReadTaskOperationResult> ReadAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            return ValueTask.FromResult(new ReadTaskOperationResult(
                new TaskOperationSnapshot(
                    attempt,
                    sequence,
                    new TaskOperationEdge(CapabilityTarget, "request-synapse", 1, "response-synapse", 1),
                    RequestRef,
                    TaskOperationPhase.Prepared,
                    ResponsePayload: null,
                    RedactedSummary: null)));
        }

        public ValueTask<TaskOperationSnapshot> TransitionAsync(
            OwnerId owner,
            NeuronId task,
            AttemptId attempt,
            int sequence,
            TaskOperationPhase expectedPhase,
            TaskOperationPhase phase,
            ProtectedPayloadReference? responsePayload,
            string? redactedSummary,
            CancellationToken cancellationToken)
        {
            TransitionCalls++;
            return ValueTask.FromResult(new TaskOperationSnapshot(
                attempt,
                sequence,
                new TaskOperationEdge(CapabilityTarget, "request-synapse", 1, "response-synapse", 1),
                RequestRef,
                phase,
                responsePayload,
                redactedSummary));
        }
    }

    private sealed record SnapshotResponse(
        string Attempt,
        int Sequence,
        EdgeResponse Edge,
        ProtectedReferenceResponse RequestPayload,
        int Phase,
        ProtectedReferenceResponse? ResponsePayload,
        string? RedactedSummary);

    private sealed record EdgeResponse(
        string TargetType,
        string TargetOwner,
        string TargetName,
        string RequestId,
        int RequestVersion,
        string ResponseId,
        int ResponseVersion);

    private sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);

    private sealed record ReadResponse(SnapshotResponse? Operation);
}
