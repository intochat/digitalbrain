using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class HttpBehaviorHostClientExecuteOwnerIsolation
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly OwnerId MetadataOwner = new("owner-metadata");
    private static readonly OwnerId TaskOwner = new("owner-task");
    private static readonly BehaviorId Behavior = new("com.digitalbrain.execute-owner");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(TaskOwner, "execute-task");
    private static readonly AttemptId Attempt = new(Guid.Parse("11111111111111111111111111111111"));
    private static readonly BehaviorExecutionId Execution =
        new(Guid.Parse("22222222222222222222222222222222"));
    private static readonly ProtectedPayloadReference TriggerPayload =
        new(Guid.Parse("33333333333333333333333333333333"), new DateTimeOffset(2026, 7, 31, 15, 0, 0, TimeSpan.Zero));

    private const string ArtifactHash =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact(DisplayName =
        "HttpBehaviorHostClient execute JSON posts metadata owner and task owner without substituting either")]
    public async Task ExecuteJsonPostsDistinctMetadataAndTaskOwners()
    {
        string? capturedBody = null;
        using var handler = new CaptureHandler(request =>
        {
            capturedBody = request.Body;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"succeeded":true,"outcome":"executed"}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://behavior-host/") };
        var client = new HttpBehaviorHostClient(http);

        var outcome = await client.ExecuteAsync(
            new BehaviorHostExecuteCommand(
                new BehaviorExecutionMetadata(
                    MetadataOwner,
                    Behavior,
                    new BehaviorRevisionId(ArtifactHash),
                    Execution),
                ArtifactHash,
                TaskNeuron,
                Attempt,
                "SampleTrigger",
                TriggerPayload,
                Capabilities: [],
                new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero)),
            TestContext.Current.CancellationToken);

        Assert.True(outcome.Succeeded, outcome.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(capturedBody));

        using var document = JsonDocument.Parse(capturedBody!);
        var root = document.RootElement;
        Assert.Equal(MetadataOwner.Value, root.GetProperty("owner").GetString());
        Assert.Equal(TaskOwner.Value, root.GetProperty("taskOwner").GetString());
        Assert.NotEqual(root.GetProperty("owner").GetString(), root.GetProperty("taskOwner").GetString());
        Assert.Equal(TaskNeuron.Type, root.GetProperty("taskType").GetString());
        Assert.Equal(TaskNeuron.Name, root.GetProperty("taskName").GetString());
        Assert.Equal(Attempt.Value.ToString("N"), root.GetProperty("attempt").GetString());
        Assert.Equal(TriggerPayload.Id.ToString("N"), root.GetProperty("triggerPayloadId").GetString());
        Assert.False(root.TryGetProperty("triggerJson", out _));
    }

    [Fact(DisplayName =
        "Execute request JSON missing taskOwner leaves TaskOwner null and fails OwnerId without inheriting metadata owner")]
    public void MissingTaskOwnerFailsClosedWithoutInheritingMetadataOwner()
    {
        const string legacyMissingTaskOwnerJson =
            """
            {
              "owner": "owner-metadata",
              "behavior": "com.digitalbrain.execute-owner",
              "revision": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "execution": "22222222222222222222222222222222",
              "artifactHash": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
              "triggerTypeName": "SampleTrigger",
              "triggerJson": "{\"Label\":\"l2\"}"
            }
            """;

        var body = JsonSerializer.Deserialize<ExecuteRequestWire>(legacyMissingTaskOwnerJson, JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("owner-metadata", body.Owner);
        Assert.Null(body.TaskOwner);

        var metadataOwner = new OwnerId(body.Owner!);
        Assert.Equal("owner-metadata", metadataOwner.Value);

        var failure = Assert.Throws<ArgumentNullException>(() => new OwnerId(body.TaskOwner!));
        Assert.Equal("value", failure.ParamName);
        Assert.NotEqual(metadataOwner.Value, body.TaskOwner);
    }

    private sealed record ExecuteRequestWire(
        string? Owner,
        string? Behavior,
        string? Revision,
        string? Execution,
        string? ArtifactHash,
        string? TriggerTypeName,
        string? TaskType,
        string? TaskOwner,
        string? TaskName,
        string? Attempt,
        string? TriggerPayloadId,
        DateTimeOffset? TriggerPayloadExpiresAt,
        object[]? Capabilities,
        DateTimeOffset UtcNow);

    private sealed class CaptureHandler(Func<RecordedRequest, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return responder(new RecordedRequest(request.Method, request.RequestUri!, body));
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);
}
