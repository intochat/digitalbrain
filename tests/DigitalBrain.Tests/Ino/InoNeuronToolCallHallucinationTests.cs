using DigitalBrain.Core;
using DigitalBrain.Ino;
using DigitalBrain.TestKit;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Ino;

public sealed class InoNeuronToolCallHallucinationTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IChatClient, QueuedInoChatClient>());

    [Fact]
    public async Task Replaces_a_hallucinated_tool_call_blob_with_a_graceful_fallback()
    {
        QueuedInoChatClient.Replies.Clear();
        QueuedInoChatClient.Replies.Enqueue(
            "```json\n{\n  \"name\": \"gmail_get_messages\",\n  \"arguments\": {\n    \"query\": \"last\"\n  }\n}\n```");

        var ino = Grain<IInoNeuron>("ino-hallucination");
        await ino.FireAsync(new InoRequest("Get my last gmail", "session-hallucination"));

        var response = (await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>().Last();
        Assert.DoesNotContain("\"name\"", response.Response);
        Assert.DoesNotContain("\"arguments\"", response.Response);
        Assert.Contains("try again", response.Response, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leaves_a_normal_reply_untouched()
    {
        QueuedInoChatClient.Replies.Clear();
        QueuedInoChatClient.Replies.Enqueue("Your most recent email is from Alice about Project Update.");

        var ino = Grain<IInoNeuron>("ino-hallucination-normal");
        await ino.FireAsync(new InoRequest("Get my last gmail", "session-hallucination-normal"));

        var response = (await ino.GetOutgoingTimelineAsync()).OfType<InoResponse>().Last();
        Assert.Equal("Your most recent email is from Alice about Project Update.", response.Response);
    }
}
