using System.Net;
using System.Text;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class HttpBehaviorHostClientExecuteOwnerIsolation
{
    private static readonly OwnerId MetadataOwner = new("owner-metadata");
    private static readonly OwnerId TaskOwner = new("owner-task");
    private static readonly BehaviorId Behavior = new("com.digitalbrain.execute-owner");
    private static readonly NeuronId TaskNeuron = NeuronId.For<ITask>(TaskOwner, "execute-task");
    private static readonly NeuronId WorkerNeuron = NeuronId.For<IWorker>(MetadataOwner, "execute-worker");
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
                new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero),
                WorkerNeuron),
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
