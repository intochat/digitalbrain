using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Host;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class HttpBehaviorHostBrokerClientTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly OwnerId BoundOwner = new("owner-bound");
    private static readonly NeuronId BoundTask = NeuronId.For<ITask>(BoundOwner, "broker-task");
    private static readonly AttemptId BoundAttempt = new(Guid.Parse("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
    private static readonly NeuronId BoundWorker = NeuronId.For<IWorker>(BoundOwner, "broker-worker");
    private static readonly NeuronId CapabilityTarget = NeuronId.For<ITask>(BoundOwner, "capability-target");
    private const string RequestSynapseId = "request-synapse";
    private const string ResponseSynapseId = "response-synapse";
    private static readonly Guid PayloadId = Guid.Parse("dddddddddddddddddddddddddddddddd");
    private static readonly DateTimeOffset PayloadExpiresAt = new(2026, 7, 31, 12, 0, 0, TimeSpan.Zero);

    [Fact(DisplayName = "Factory binds owner/task/attempt and payload store/load round-trip over reverse broker HTTP")]
    public async Task FactoryBindsExecutionAndPayloadStoreLoadRoundTrip()
    {
        var plaintext = "hello-payload"u8.ToArray();
        var storedReference = new ProtectedPayloadReference(PayloadId, PayloadExpiresAt);

        using var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/behaviors/broker/payloads/store", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    id = PayloadId.ToString("N"),
                    expiresAt = PayloadExpiresAt
                });
            }

            if (path.EndsWith("/v1/behaviors/broker/payloads/load", StringComparison.Ordinal))
            {
                return JsonResponse(new
                {
                    contentBase64 = Convert.ToBase64String(plaintext)
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var factory = CreateFactory(handler);
        var client = factory.Create(BoundOwner, BoundTask, BoundAttempt, BoundWorker);

        var stored = await client.StorePayloadAsync(
            BoundOwner,
            BoundTask,
            BoundAttempt,
            plaintext,
            CancellationToken.None);

        Assert.Equal(storedReference.Id, stored.Id);
        Assert.Equal(storedReference.ExpiresAt, stored.ExpiresAt);

        var loaded = await client.LoadPayloadAsync(
            BoundOwner,
            BoundTask,
            BoundAttempt,
            storedReference,
            CancellationToken.None);

        Assert.Equal(plaintext, loaded.ToArray());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.EndsWith("/v1/behaviors/broker/payloads/store", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/v1/behaviors/broker/payloads/load", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);

        using var storeDoc = JsonDocument.Parse(handler.Requests[0].Body);
        AssertCommonIdentity(storeDoc.RootElement);
        Assert.Equal(Convert.ToBase64String(plaintext), storeDoc.RootElement.GetProperty("contentBase64").GetString());

        using var loadDoc = JsonDocument.Parse(handler.Requests[1].Body);
        AssertCommonIdentity(loadDoc.RootElement);
        AssertProtectedReference(loadDoc.RootElement.GetProperty("reference"), PayloadId, PayloadExpiresAt);
    }

    [Fact(DisplayName = "Prepare/read/transition preserve DTO fidelity including nullable read and completed response reference")]
    public async Task PrepareReadTransitionPreserveDtoFidelity()
    {
        var requestRef = new ProtectedPayloadReference(PayloadId, PayloadExpiresAt);
        var responseRef = new ProtectedPayloadReference(Guid.Parse("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"), null);
        var edge = new TaskOperationEdge(CapabilityTarget, RequestSynapseId, 1, ResponseSynapseId, 2);

        var preparedSnapshot = new TaskOperationSnapshot(
            BoundAttempt,
            Sequence: 7,
            edge,
            requestRef,
            TaskOperationPhase.Prepared,
            ResponsePayload: null,
            RedactedSummary: "prepare-summary");

        var completedSnapshot = new TaskOperationSnapshot(
            BoundAttempt,
            Sequence: 8,
            edge,
            requestRef,
            TaskOperationPhase.Completed,
            ResponsePayload: responseRef,
            RedactedSummary: "transition-summary");

        using var handler = new RecordingHttpMessageHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/v1/behaviors/broker/operations/prepare", StringComparison.Ordinal))
            {
                return JsonResponse(SnapshotWire(preparedSnapshot));
            }

            if (path.EndsWith("/v1/behaviors/broker/operations/read", StringComparison.Ordinal))
            {
                return JsonResponse(new { operation = (object?)null });
            }

            if (path.EndsWith("/v1/behaviors/broker/operations/transition", StringComparison.Ordinal))
            {
                return JsonResponse(SnapshotWire(completedSnapshot));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var client = CreateFactory(handler).Create(BoundOwner, BoundTask, BoundAttempt, BoundWorker);

        var prepared = await client.PrepareAsync(
            new PrepareTaskOperation(BoundAttempt, Sequence: 7, edge, requestRef),
            CancellationToken.None);

        Assert.Equal(preparedSnapshot, prepared);

        var read = await client.ReadAsync(
            new ReadTaskOperation(BoundAttempt, Sequence: 7),
            CancellationToken.None);

        Assert.Null(read.Operation);

        var transitioned = await client.TransitionAsync(
            new TransitionTaskOperation(
                BoundAttempt,
                Sequence: 8,
                TaskOperationPhase.Prepared,
                TaskOperationPhase.Completed,
                responseRef,
                RedactedSummary: "transition-summary"),
            CancellationToken.None);

        Assert.Equal(completedSnapshot, transitioned);

        Assert.Equal(3, handler.Requests.Count);
        Assert.EndsWith("/v1/behaviors/broker/operations/prepare", handler.Requests[0].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/v1/behaviors/broker/operations/read", handler.Requests[1].Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith("/v1/behaviors/broker/operations/transition", handler.Requests[2].Uri.AbsolutePath, StringComparison.Ordinal);

        using var prepareDoc = JsonDocument.Parse(handler.Requests[0].Body);
        AssertCommonIdentity(prepareDoc.RootElement);
        Assert.Equal(7, prepareDoc.RootElement.GetProperty("sequence").GetInt32());
        AssertEdge(prepareDoc.RootElement.GetProperty("edge"), edge);
        AssertProtectedReference(prepareDoc.RootElement.GetProperty("requestPayload"), PayloadId, PayloadExpiresAt);

        using var readDoc = JsonDocument.Parse(handler.Requests[1].Body);
        AssertCommonIdentity(readDoc.RootElement);
        Assert.Equal(7, readDoc.RootElement.GetProperty("sequence").GetInt32());

        using var transitionDoc = JsonDocument.Parse(handler.Requests[2].Body);
        AssertCommonIdentity(transitionDoc.RootElement);
        Assert.Equal(8, transitionDoc.RootElement.GetProperty("sequence").GetInt32());
        Assert.Equal((int)TaskOperationPhase.Prepared, transitionDoc.RootElement.GetProperty("expectedPhase").GetInt32());
        Assert.Equal((int)TaskOperationPhase.Completed, transitionDoc.RootElement.GetProperty("phase").GetInt32());
        AssertProtectedReference(transitionDoc.RootElement.GetProperty("responsePayload"), responseRef.Id, null);
        Assert.Equal("transition-summary", transitionDoc.RootElement.GetProperty("redactedSummary").GetString());
    }

    [Fact(DisplayName = "Dispatch carries exact BehaviorCapabilityEdge and request reference and returns only protected response reference")]
    public async Task DispatchPreservesExactEdgeFidelity()
    {
        var edge = new BehaviorCapabilityEdge(CapabilityTarget, RequestSynapseId, 4, ResponseSynapseId, 5);
        var requestPayload = new ProtectedPayloadReference(PayloadId, PayloadExpiresAt);
        var responsePayload = new ProtectedPayloadReference(Guid.Parse("ffffffffffffffffffffffffffffffff"), null);

        using var handler = new RecordingHttpMessageHandler(request =>
        {
            Assert.EndsWith("/v1/behaviors/broker/dispatch", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);
            return JsonResponse(new
            {
                id = responsePayload.Id.ToString("N"),
                expiresAt = (DateTimeOffset?)null
            });
        });

        var client = CreateFactory(handler).Create(BoundOwner, BoundTask, BoundAttempt, BoundWorker);

        var dispatched = await client.DispatchAsync(edge, requestPayload, CancellationToken.None);

        Assert.Equal(responsePayload.Id, dispatched.Id);
        Assert.Null(dispatched.ExpiresAt);

        Assert.Single(handler.Requests);
        using var doc = JsonDocument.Parse(handler.Requests[0].Body);
        AssertCommonIdentity(doc.RootElement);
        AssertEdge(doc.RootElement.GetProperty("edge"), CapabilityTarget, RequestSynapseId, 4, ResponseSynapseId, 5);
        AssertProtectedReference(doc.RootElement.GetProperty("requestPayload"), PayloadId, PayloadExpiresAt);
    }

    [Fact(DisplayName = "Cross-owner/task/attempt misuse fails before any reverse broker HTTP request is sent")]
    public async Task CrossIdentityMisuseFailsBeforeHttp()
    {
        using var handler = new RecordingHttpMessageHandler(_ =>
            throw new InvalidOperationException("HTTP must not be reached for identity misuse."));

        var client = CreateFactory(handler).Create(BoundOwner, BoundTask, BoundAttempt, BoundWorker);
        var otherOwner = new OwnerId("owner-other");
        var otherTask = NeuronId.For<ITask>(BoundOwner, "other-task");
        var otherAttempt = new AttemptId(Guid.Parse("11111111111111111111111111111111"));
        var payload = new ProtectedPayloadReference(PayloadId, null);
        var edge = new TaskOperationEdge(CapabilityTarget, RequestSynapseId, 1, ResponseSynapseId, 1);

        await Assert.ThrowsAsync<BehaviorHostException>(async () => await client.StorePayloadAsync(
            otherOwner,
            BoundTask,
            BoundAttempt,
            "x"u8.ToArray(),
            CancellationToken.None));

        await Assert.ThrowsAsync<BehaviorHostException>(async () => await client.LoadPayloadAsync(
            BoundOwner,
            otherTask,
            BoundAttempt,
            payload,
            CancellationToken.None));

        await Assert.ThrowsAsync<BehaviorHostException>(async () => await client.PrepareAsync(
            new PrepareTaskOperation(otherAttempt, Sequence: 1, edge, payload),
            CancellationToken.None));

        await Assert.ThrowsAsync<BehaviorHostException>(async () => await client.ReadAsync(
            new ReadTaskOperation(otherAttempt, Sequence: 1),
            CancellationToken.None));

        await Assert.ThrowsAsync<BehaviorHostException>(async () => await client.TransitionAsync(
            new TransitionTaskOperation(
                otherAttempt,
                Sequence: 1,
                TaskOperationPhase.Prepared,
                TaskOperationPhase.Completed,
                payload,
                RedactedSummary: "summary"),
            CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact(DisplayName =
        "AddBehaviorHostEngine fails closed without broker address and uses HTTP reverse broker for absolute address")]
    public void AddBehaviorHostEngineRegistersBrokerFactoryOnlyForValidAbsoluteAddress()
    {
        var absentConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Security:StateProtectionKey"] = Convert.ToBase64String(new byte[32])
            })
            .Build();

        var absent = new ServiceCollection();
        absent.AddBehaviorHostEngine(absentConfiguration);
        using var absentProvider = absent.BuildServiceProvider();
        Assert.Null(absentProvider.GetService<IBehaviorHostBrokerClientFactory>());

        var presentConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Security:StateProtectionKey"] = Convert.ToBase64String(new byte[32]),
                [BehaviorHostHosting.BrokerBaseAddressConfigurationKey] = "https://broker.example/",
                [BehaviorHostHosting.BrokerCredentialConfigurationKey] = "unit-test-broker-credential",
            })
            .Build();

        var present = new ServiceCollection();
        present.AddBehaviorHostEngine(presentConfiguration);
        using var presentProvider = present.BuildServiceProvider();

        var factory = presentProvider.GetService<IBehaviorHostBrokerClientFactory>();
        Assert.IsType<HttpBehaviorHostBrokerClientFactory>(factory);

        var httpClientFactory = presentProvider.GetRequiredService<IHttpClientFactory>();
        using var named = httpClientFactory.CreateClient(BehaviorHostHosting.BrokerHttpClientName);
        Assert.Equal(new Uri("https://broker.example/"), named.BaseAddress);
        Assert.True(named.DefaultRequestHeaders.Contains(BehaviorHostHosting.BrokerCredentialHeaderName));
        Assert.Equal(
            "unit-test-broker-credential",
            named.DefaultRequestHeaders.GetValues(BehaviorHostHosting.BrokerCredentialHeaderName).Single());

        Assert.Throws<InvalidOperationException>(() =>
        {
            var incomplete = new ServiceCollection();
            incomplete.AddBehaviorHostEngine(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DigitalBrain:Security:StateProtectionKey"] = Convert.ToBase64String(new byte[32]),
                    [BehaviorHostHosting.BrokerBaseAddressConfigurationKey] = "https://broker.example/",
                })
                .Build());
            using var incompleteProvider = incomplete.BuildServiceProvider();
            _ = incompleteProvider.GetService<IBehaviorHostBrokerClientFactory>();
            _ = incompleteProvider.GetRequiredService<IHttpClientFactory>()
                .CreateClient(BehaviorHostHosting.BrokerHttpClientName);
        });
    }

    [Fact(DisplayName = "an emit request carries the attempt's hop budget across the wire so the silo can charge it")]
    public async Task EmitFactCarriesTheHopBudgetAcrossTheWire()
    {
        using var handler = new RecordingHttpMessageHandler(static _ =>
            JsonResponse(new { outcome = BehaviorFactEmission.Emitted }));

        var factory = CreateFactory(handler);
        var client = factory.Create(BoundOwner, BoundTask, BoundAttempt, BoundWorker);

        await client.EmitFactAsync(
            new BehaviorId("com.digitalbrain.speaker"),
            "test.spoken-fact",
            Encoding.UTF8.GetBytes("""{"Label":"carried"}"""),
            hopsRemaining: 4,
            CancellationToken.None);

        var recorded = Assert.Single(handler.Requests);
        Assert.EndsWith("/v1/behaviors/broker/emit", recorded.Uri.AbsolutePath, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(recorded.Body);
        var root = document.RootElement;
        AssertCommonIdentity(root);
        Assert.Equal("com.digitalbrain.speaker", root.GetProperty("behavior").GetString());
        Assert.Equal("test.spoken-fact", root.GetProperty("emitAlias").GetString());
        Assert.Equal(4, root.GetProperty("hops").GetInt32());
    }

    private static IBehaviorHostBrokerClientFactory CreateFactory(RecordingHttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Security:StateProtectionKey"] = Convert.ToBase64String(new byte[32]),
                [BehaviorHostHosting.BrokerBaseAddressConfigurationKey] = "https://broker.test/",
                [BehaviorHostHosting.BrokerCredentialConfigurationKey] = "unit-test-broker-credential",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddBehaviorHostEngine(configuration);
        services.ConfigureAll<HttpClientFactoryOptions>(options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(builder =>
            {
                builder.PrimaryHandler = handler;
            });
        });

        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IBehaviorHostBrokerClientFactory>();
    }

    private static void AssertCommonIdentity(JsonElement root)
    {
        Assert.Equal(BoundOwner.Value, root.GetProperty("owner").GetString());
        Assert.Equal(BoundTask.Type, root.GetProperty("taskType").GetString());
        Assert.Equal(BoundTask.Owner.Value, root.GetProperty("taskOwner").GetString());
        Assert.Equal(BoundTask.Name, root.GetProperty("taskName").GetString());
        Assert.Equal(BoundAttempt.Value.ToString("N"), root.GetProperty("attempt").GetString());
    }

    private static void AssertProtectedReference(JsonElement element, Guid id, DateTimeOffset? expiresAt)
    {
        Assert.Equal(id.ToString("N"), element.GetProperty("id").GetString());
        if (expiresAt is null)
        {
            Assert.True(
                element.GetProperty("expiresAt").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                || element.GetProperty("expiresAt").GetString() is null);
        }
        else
        {
            Assert.Equal(expiresAt.Value, element.GetProperty("expiresAt").GetDateTimeOffset());
        }
    }

    private static void AssertEdge(JsonElement element, TaskOperationEdge edge)
        => AssertEdge(
            element,
            edge.Target,
            edge.RequestSynapseId,
            edge.RequestSchemaVersion,
            edge.ResponseSynapseId,
            edge.ResponseSchemaVersion);

    private static void AssertEdge(
        JsonElement element,
        NeuronId target,
        string requestId,
        int requestVersion,
        string responseId,
        int responseVersion)
    {
        Assert.Equal(target.Type, element.GetProperty("targetType").GetString());
        Assert.Equal(target.Owner.Value, element.GetProperty("targetOwner").GetString());
        Assert.Equal(target.Name, element.GetProperty("targetName").GetString());
        Assert.Equal(requestId, element.GetProperty("requestId").GetString());
        Assert.Equal(requestVersion, element.GetProperty("requestVersion").GetInt32());
        Assert.Equal(responseId, element.GetProperty("responseId").GetString());
        Assert.Equal(responseVersion, element.GetProperty("responseVersion").GetInt32());
    }

    private static object SnapshotWire(TaskOperationSnapshot snapshot)
    {
        return new
        {
            attempt = snapshot.Attempt.Value.ToString("N"),
            sequence = snapshot.Sequence,
            edge = new
            {
                targetType = snapshot.Edge.Target.Type,
                targetOwner = snapshot.Edge.Target.Owner.Value,
                targetName = snapshot.Edge.Target.Name,
                requestId = snapshot.Edge.RequestSynapseId,
                requestVersion = snapshot.Edge.RequestSchemaVersion,
                responseId = snapshot.Edge.ResponseSynapseId,
                responseVersion = snapshot.Edge.ResponseSchemaVersion
            },
            requestPayload = new
            {
                id = snapshot.RequestPayload.Id.ToString("N"),
                expiresAt = snapshot.RequestPayload.ExpiresAt
            },
            phase = (int)snapshot.Phase,
            responsePayload = snapshot.ResponsePayload is null
                ? null
                : new
                {
                    id = snapshot.ResponsePayload.Value.Id.ToString("N"),
                    expiresAt = snapshot.ResponsePayload.Value.ExpiresAt
                },
            redactedSummary = snapshot.RedactedSummary
        };
    }

    private static HttpResponseMessage JsonResponse(object body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, body));
            return _responder(request);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
