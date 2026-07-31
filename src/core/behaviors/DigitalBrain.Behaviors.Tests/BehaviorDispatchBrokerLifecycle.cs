using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorDispatchBrokerLifecycle(BehaviorDispatchFixture fixture)
{
    private const string ValidCredential = "lifecycle-dispatch-broker-credential";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "real silo HTTP dispatch through Worker authority delivers once to harness neuron, stores response, and completed replay performs no second provider call")]
    public async Task RealSiloDispatchThroughWorkerAuthorityOnceAndReplaySkipsProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        DispatchHarness.Reset();
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("dispatch-lifecycle-worker");
        var task = brain.Neuron<ITask>("dispatch-lifecycle-task");
        var probe = brain.Neuron<IDispatchProbe>("probe");
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "dispatch-lifecycle",
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
        var attempt = snapshot.ActiveAttempt.Value;

        var catalog = brain.Cluster.ClientServices.GetRequiredService<ActiveCapabilityCatalog>();
        var payloads = new GrainBehaviorProtectedPayloadAccess(brain.Cluster.Client);
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(
            brain.Cluster.Client,
            catalog,
            payloads);
        var operations = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);

        await using var host = await StartHostAsync(dispatch, operations, payloads, cancellationToken);
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            BehaviorBrokerContract.CredentialHeaderName,
            ValidCredential);

        var plaintext = DispatchHarness.SerializeRequest("dispatch-once");
        using var store = await PostAuthorizedAsync(
            http,
            "/v1/behaviors/broker/payloads/store",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                contentBase64 = Convert.ToBase64String(plaintext),
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, store.StatusCode);
        var requestRef = await store.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(requestRef);

        var edge = new BehaviorCapabilityEdge(
            new NeuronId(DispatchHarness.NeuronContractId, task.Id.Owner, probe.Id.Name),
            DispatchHarness.RequestContractId,
            1,
            DispatchHarness.ResponseContractId,
            1);

        using var firstHttp = await PostAuthorizedAsync(
            http,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                edge = new
                {
                    targetType = edge.Target.Type,
                    targetOwner = edge.Target.Owner.Value,
                    targetName = edge.Target.Name,
                    requestId = edge.RequestSynapseId,
                    requestVersion = edge.RequestSchemaVersion,
                    responseId = edge.ResponseSynapseId,
                    responseVersion = edge.ResponseSchemaVersion,
                },
                requestPayload = new { id = requestRef.Id, expiresAt = requestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, firstHttp.StatusCode);
        var responseRef = await firstHttp.Content.ReadFromJsonAsync<ProtectedReferenceResponse>(
            cancellationToken: cancellationToken);
        Assert.NotNull(responseRef);
        Assert.False(string.IsNullOrWhiteSpace(responseRef.Id));
        Assert.Equal(1, DispatchHarness.DeliveryCount);
        Assert.Equal("dispatch-once", DispatchHarness.LastText);

        using var load = await PostAuthorizedAsync(
            http,
            "/v1/behaviors/broker/payloads/load",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                reference = new { id = responseRef.Id, expiresAt = responseRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.OK, load.StatusCode);
        var loaded = await load.Content.ReadFromJsonAsync<LoadPayloadResponse>(cancellationToken: cancellationToken);
        Assert.NotNull(loaded);
        var responseBytes = Convert.FromBase64String(loaded.ContentBase64);
        var decoded = JsonSerializer.Deserialize<DispatchProbeResponse>(responseBytes, JsonOptions);
        Assert.NotNull(decoded);
        Assert.Equal("dispatch-once", decoded.Text);

        // Seed a completed durable operation from the first protected response so replay composes
        // BehaviorOperationBroker without a second provider delivery.
        var brokerClient = new HttpBehaviorHostBrokerClient(http, task.Id.Owner, task.Id, attempt);
        var history = new TaskOwnedOperationHistory(task.Id, attempt, brokerClient);
        var prepared = await history.PrepareAsync(
            sequence: 0,
            edge,
            new ProtectedPayloadReference(Guid.ParseExact(requestRef.Id, "N"), requestRef.ExpiresAt),
            cancellationToken);
        Assert.Equal(BehaviorOperationPhase.Prepared, prepared.Phase);
        var dispatched = await history.TransitionAsync(
            prepared.Identity,
            BehaviorOperationPhase.Prepared,
            BehaviorOperationPhase.Dispatched,
            responsePayload: null,
            redactedSummary: null,
            cancellationToken);
        Assert.Equal(BehaviorOperationPhase.Dispatched, dispatched.Phase);
        var completedSeed = await history.TransitionAsync(
            dispatched.Identity,
            BehaviorOperationPhase.Dispatched,
            BehaviorOperationPhase.Completed,
            new ProtectedPayloadReference(Guid.ParseExact(responseRef.Id, "N"), responseRef.ExpiresAt),
            redactedSummary: null,
            cancellationToken);
        Assert.Equal(BehaviorOperationPhase.Completed, completedSeed.Phase);
        Assert.Equal(1, DispatchHarness.DeliveryCount);

        var brokerA = new BehaviorOperationBroker(history, edge, brokerClient);
        var completed = await brokerA.ExecuteAsync(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion,
            new ProtectedPayloadReference(Guid.ParseExact(requestRef.Id, "N"), requestRef.ExpiresAt),
            cancellationToken);
        Assert.Equal(BehaviorOperationPhase.Completed, completed.Phase);
        Assert.NotNull(completed.ResponsePayload);
        Assert.Equal(
            Guid.ParseExact(responseRef.Id, "N"),
            completed.ResponsePayload!.Value.Id);
        Assert.Equal(1, DispatchHarness.DeliveryCount);

        // Fresh broker instance restarts sequence claim at 0 and must replay completed history.
        var brokerB = new BehaviorOperationBroker(history, edge, brokerClient);
        var replayed = await brokerB.ExecuteAsync(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion,
            new ProtectedPayloadReference(Guid.ParseExact(requestRef.Id, "N"), requestRef.ExpiresAt),
            cancellationToken);
        Assert.Equal(BehaviorOperationPhase.Completed, replayed.Phase);
        Assert.Equal(completed.ResponsePayload, replayed.ResponsePayload);
        Assert.Equal(1, DispatchHarness.DeliveryCount);

        using var foreign = await PostAuthorizedAsync(
            http,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                edge = new
                {
                    targetType = "unknown.neuron",
                    targetOwner = task.Id.Owner.Value,
                    targetName = "nope",
                    requestId = edge.RequestSynapseId,
                    requestVersion = 1,
                    responseId = edge.ResponseSynapseId,
                    responseVersion = 1,
                },
                requestPayload = new { id = requestRef.Id, expiresAt = requestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("unknown-target-neuron", (await foreign.Content.ReadAsStringAsync(cancellationToken)).Trim());
        Assert.Equal(1, DispatchHarness.DeliveryCount);

        using var versionDrift = await PostAuthorizedAsync(
            http,
            "/v1/behaviors/broker/dispatch",
            new
            {
                owner = task.Id.Owner.Value,
                taskType = task.Id.Type,
                taskOwner = task.Id.Owner.Value,
                taskName = task.Id.Name,
                attempt = attempt.Value.ToString("N"),
                edge = new
                {
                    targetType = edge.Target.Type,
                    targetOwner = edge.Target.Owner.Value,
                    targetName = edge.Target.Name,
                    requestId = edge.RequestSynapseId,
                    requestVersion = 2,
                    responseId = edge.ResponseSynapseId,
                    responseVersion = 1,
                },
                requestPayload = new { id = requestRef.Id, expiresAt = requestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, versionDrift.StatusCode);
        var versionReason = (await versionDrift.Content.ReadAsStringAsync(cancellationToken)).Trim();
        Assert.True(
            versionReason is "unknown-request-synapse" or "incompatible-request-version",
            versionReason);
        Assert.Equal(1, DispatchHarness.DeliveryCount);

        using var cancelledCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await cancelledCts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await PostAuthorizedAsync(
                http,
                "/v1/behaviors/broker/dispatch",
                new
                {
                    owner = task.Id.Owner.Value,
                    taskType = task.Id.Type,
                    taskOwner = task.Id.Owner.Value,
                    taskName = task.Id.Name,
                    attempt = attempt.Value.ToString("N"),
                    edge = new
                    {
                        targetType = edge.Target.Type,
                        targetOwner = edge.Target.Owner.Value,
                        targetName = edge.Target.Name,
                        requestId = edge.RequestSynapseId,
                        requestVersion = edge.RequestSchemaVersion,
                        responseId = edge.ResponseSynapseId,
                        responseVersion = edge.ResponseSchemaVersion,
                    },
                    requestPayload = new { id = requestRef.Id, expiresAt = requestRef.ExpiresAt },
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
        IBehaviorCapabilityDispatchAccess dispatch,
        IBehaviorTaskOperationAccess operations,
        IBehaviorProtectedPayloadAccess payloads,
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
            ApplicationName = typeof(BehaviorDispatchBrokerLifecycle).Assembly.FullName,
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Services.AddRouting();
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton<IConfiguration>(configuration);
        builder.Services.AddBehaviorBrokerAuthentication(configuration);
        builder.Services.AddSingleton(dispatch);
        builder.Services.AddSingleton(operations);
        builder.Services.AddSingleton(payloads);
        var app = builder.Build();
        app.UseRouting();
        app.UseBehaviorBrokerAuthentication();
        app.MapBehaviorProtectedPayloadBroker();
        app.MapBehaviorTaskOperationBroker();
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
        public HttpClient CreateClient() => new() { BaseAddress = baseAddress };

        public ValueTask DisposeAsync() => app.DisposeAsync();
    }

    private sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);

    private sealed record LoadPayloadResponse(string ContentBase64);
}

public sealed class BehaviorDispatchFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
        brain.AddModule<BehaviorDispatchHarnessModule>();
    }
}
