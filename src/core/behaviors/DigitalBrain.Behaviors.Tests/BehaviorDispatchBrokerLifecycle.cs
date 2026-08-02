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
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Behaviors.Host;

namespace DigitalBrain.Behaviors.Tests;

public sealed class BehaviorDispatchBrokerLifecycle(BehaviorDispatchFixture fixture)
{
    private const string ValidCredential = "lifecycle-dispatch-broker-credential";

    [Fact(
        Timeout = 120_000,
        DisplayName =
            "BehaviorOperationBroker with HttpBehaviorHostBrokerClient naturally Prepare/Dispatch/Complete once; fresh broker replays sequence 0 without second delivery")]
    public async Task NaturalBrokerDispatchOnceAndReplaySkipsProvider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var probeText = $"dispatch-once-{Guid.NewGuid():N}";
        await using var brain = await fixture.CreateBrainAsync(cancellationToken);
        var worker = brain.Neuron<IWorker>("dispatch-lifecycle-worker");
        var task = brain.Neuron<ITask>("dispatch-lifecycle-task");
        var probe = brain.Neuron<IDispatchProbe>("probe");
        var activation = new BehaviorTaskActivation(
            new BehaviorId("com.digitalbrain.sample"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            contractVersion: "1",
            caseId: "dispatch-lifecycle",
            protectedPayload: new ProtectedPayloadReference(Guid.Parse("66666666-6666-6666-6666-666666666666")),
            triggerTypeName: "SampleTrigger",
            capabilities: []);
        var goal = new BehaviorActivationGoal(
            activation.BehaviorId,
            activation.Revision,
            activation.ContractVersion,
            activation.CaseId,
            activation.ProtectedPayload,
            activation.TriggerTypeName,
            activation.Capabilities);

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
        var dispatch = new GrainBehaviorCapabilityDispatchAccess(brain.Cluster.Client, catalog, payloads);
        var operations = new GrainBehaviorTaskOperationAccess(brain.Cluster.Client);

        await using var host = await StartHostAsync(dispatch, operations, payloads, cancellationToken);
        using var http = host.CreateClient();
        http.DefaultRequestHeaders.TryAddWithoutValidation(
            BehaviorBrokerContract.CredentialHeaderName,
            ValidCredential);

        var edge = new BehaviorCapabilityEdge(
            new NeuronId(DispatchHarness.NeuronContractId, task.Id.Owner, probe.Id.Name),
            DispatchHarness.RequestContractId,
            1,
            DispatchHarness.ResponseContractId,
            1);

        var brokerClient = new HttpBehaviorHostBrokerClient(http, task.Id.Owner, task.Id, attempt, worker.Id);
        var requestBytes = BehaviorPayloadJson.Serialize(
            new DispatchProbeRequest(probeText),
            typeof(DispatchProbeRequest));
        var requestRef = await brokerClient.StorePayloadAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            requestBytes,
            cancellationToken);

        var history = new TaskOwnedOperationHistory(task.Id, attempt, brokerClient);
        var firstBroker = new BehaviorOperationBroker(history, edge, brokerClient);
        var completed = await firstBroker.ExecuteAsync(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion,
            requestRef,
            cancellationToken);

        Assert.Equal(TaskOperationPhase.Completed, completed.Phase);
        Assert.NotNull(completed.ResponsePayload);
        Assert.Equal(1, DispatchHarness.CountFor(probeText));

        var durable = await history.ReadAsync(
            new BehaviorOperationIdentity(task.Id, attempt, sequence: 0),
            cancellationToken);
        Assert.NotNull(durable);
        Assert.Equal(TaskOperationPhase.Completed, durable!.Phase);

        var secondBroker = new BehaviorOperationBroker(history, edge, brokerClient);
        var replayed = await secondBroker.ExecuteAsync(
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion,
            requestRef,
            cancellationToken);
        Assert.Equal(TaskOperationPhase.Completed, replayed.Phase);
        Assert.Equal(completed.ResponsePayload, replayed.ResponsePayload);
        Assert.Equal(1, DispatchHarness.CountFor(probeText));

        var loaded = await brokerClient.LoadPayloadAsync(
            task.Id.Owner,
            task.Id,
            attempt,
            completed.ResponsePayload!.Value,
            cancellationToken);
        var decoded = BehaviorPayloadJson.Deserialize<DispatchProbeResponse>(loaded.Span);
        Assert.NotNull(decoded);
        Assert.Equal(probeText, decoded.Text);
        Assert.Equal("once-code", decoded.DetailCode);

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
                requestPayload = new { id = requestRef.Id.ToString("N"), expiresAt = requestRef.ExpiresAt },
            },
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, versionDrift.StatusCode);
        Assert.Equal(
            "incompatible-request-version",
            (await versionDrift.Content.ReadAsStringAsync(cancellationToken)).Trim());
        Assert.Equal(1, DispatchHarness.CountFor(probeText));
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
        builder.Services.AddBehaviorBrokerAuthentication(configuration, builder.Environment);
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
