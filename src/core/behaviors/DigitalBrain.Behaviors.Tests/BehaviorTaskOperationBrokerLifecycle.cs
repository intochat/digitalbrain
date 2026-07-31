using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorTaskOperationBrokerLifecycle(BehaviorsFixture fixture)
{
    private const string ValidCredential = "lifecycle-ops-broker-credential";

    [Fact(
        Timeout = 90_000,
        DisplayName = "real silo HTTP prepare/read/transition through production Worker authority is idempotent and attempt-bound")]
    public async Task RealSiloHttpPrepareTransitionReadThroughWorkerAuthority()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("lifecycle-worker");
        var task = brain.Neuron<ITask>("lifecycle-task");
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "lifecycle",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("66666666-6666-6666-6666-666666666666")));
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload);

        await task.Reference.Start(new StartTask(
            CommandId.New(),
            goal,
            worker.Id,
            new TaskPolicy(1, TimeSpan.Zero, null),
            Activation: activation))
            .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

        var snapshot = await WaitForRunningAsync(task, cancellationToken);
        Assert.NotNull(snapshot.ActiveAttempt);
        Assert.Equal(worker.Id, snapshot.Worker);
        Assert.Equal(activation, snapshot.Activation);
        var attempt = snapshot.ActiveAttempt.Value;

        var access = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);
        await using var host = await StartHostAsync(access, cancellationToken);
        using var client = host.CreateClient();

        var edge = new
        {
            targetType = "provider",
            targetOwner = task.Id.Owner.Value,
            targetName = "gmail",
            requestId = "test.provider-request",
            requestVersion = 1,
            responseId = "test.provider-response",
            responseVersion = 1,
        };
        var requestPayload = new
        {
            id = Guid.Parse("77777777-7777-7777-7777-777777777777").ToString("N"),
            expiresAt = new DateTimeOffset(2026, 7, 31, 14, 0, 0, TimeSpan.Zero),
        };

        using var prepare = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                sequence = 0,
                edge,
                requestPayload,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, prepare.StatusCode);
        var prepared = await prepare.Content.ReadFromJsonAsync<SnapshotResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(prepared);
        Assert.Equal((int)TaskOperationPhase.Prepared, prepared.Phase);
        Assert.Equal(0, prepared.Sequence);

        using var prepareReplay = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/prepare",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                sequence = 0,
                edge,
                requestPayload,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, prepareReplay.StatusCode);
        var preparedAgain = await prepareReplay.Content.ReadFromJsonAsync<SnapshotResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(preparedAgain);
        Assert.Equal(prepared.Attempt, preparedAgain.Attempt);
        Assert.Equal(prepared.Sequence, preparedAgain.Sequence);
        Assert.Equal(prepared.Phase, preparedAgain.Phase);
        Assert.Equal(prepared.RequestPayload!.Id, preparedAgain.RequestPayload!.Id);

        using var dispatched = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/transition",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                sequence = 0,
                expectedPhase = (int)TaskOperationPhase.Prepared,
                phase = (int)TaskOperationPhase.Dispatched,
                responsePayload = (object?)null,
                redactedSummary = (string?)null,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, dispatched.StatusCode);
        var dispatchedBody = await dispatched.Content.ReadFromJsonAsync<SnapshotResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(dispatchedBody);
        Assert.Equal((int)TaskOperationPhase.Dispatched, dispatchedBody.Phase);

        var responseId = Guid.Parse("88888888-8888-8888-8888-888888888888").ToString("N");
        using var completed = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/transition",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                sequence = 0,
                expectedPhase = (int)TaskOperationPhase.Dispatched,
                phase = (int)TaskOperationPhase.Completed,
                responsePayload = new { id = responseId, expiresAt = (DateTimeOffset?)null },
                redactedSummary = "completed",
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var completedBody = await completed.Content.ReadFromJsonAsync<SnapshotResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(completedBody);
        Assert.Equal((int)TaskOperationPhase.Completed, completedBody.Phase);
        Assert.Equal(responseId, completedBody.ResponsePayload!.Id);

        using var read = await PostAuthorizedAsync(
            client,
            "/v1/behaviors/broker/operations/read",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                sequence = 0,
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var readBody = await read.Content.ReadFromJsonAsync<ReadResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(readBody?.Operation);
        Assert.Equal((int)TaskOperationPhase.Completed, readBody.Operation.Phase);
        Assert.Equal(responseId, readBody.Operation.ResponsePayload!.Id);

        var bound = await task.Reference.Read();
        Assert.Equal(TaskState.Running, bound.State);
        Assert.Equal(worker.Id, bound.Worker);
        Assert.Equal(attempt, bound.ActiveAttempt);
        Assert.Equal(activation, bound.Activation);

        using var cancelledCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelledCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PostAuthorizedAsync(
                client,
                "/v1/behaviors/broker/operations/read",
                new
                {
                    owner = task.Id.Owner.Value,
                    taskType = task.Id.Type,
                    taskOwner = task.Id.Owner.Value,
                    taskName = task.Id.Name,
                    attempt = attempt.Value.ToString("N"),
                    sequence = 0,
                },
                cancelledCts.Token));
    }

    private static async Task<TaskSnapshot> WaitForRunningAsync(
        TestNeuron<ITask> task,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await task.Reference.Read();
            if (snapshot.State == TaskState.Running && snapshot.ActiveAttempt is not null)
            {
                return snapshot;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
        }

        var final = await task.Reference.Read();
        throw new TimeoutException($"Task stayed in {final.State} instead of Running.");
    }

    private static async Task<HttpResponseMessage> PostAuthorizedAsync(
        HttpClient client,
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.TryAddWithoutValidation(BehaviorBrokerContract.CredentialHeaderName, ValidCredential);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<RunningHost> StartHostAsync(
        IBehaviorTaskOperationAccess access,
        CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [BehaviorBrokerContract.CredentialConfigurationKey] = ValidCredential,
            })
            .Build();

        var port = GetFreeTcpPort();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(BehaviorTaskOperationBrokerLifecycle).Assembly.FullName,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration);
        builder.Services.AddSingleton(access);
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
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
        public HttpClient CreateClient() => new() { BaseAddress = baseAddress };

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private sealed record SnapshotResponse(
        string Attempt,
        int Sequence,
        int Phase,
        ProtectedReferenceResponse? RequestPayload,
        ProtectedReferenceResponse? ResponsePayload,
        string? RedactedSummary);

    private sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);

    private sealed record ReadResponse(SnapshotResponse? Operation);
}
