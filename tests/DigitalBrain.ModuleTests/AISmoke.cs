using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AISmoke(ModuleFixture fixture)
{
    private const string ModelName = "typed-model";
    private const string UserPrompt = "hello";
    private const string ScriptedReply = "typed response";

    [Fact(DisplayName = "typed LLM returns the scripted edge response")]
    public async Task TypedLlmReturnsTheScriptedResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply(ScriptedReply);

        var response = await test.Client.Get<ILlama32>(ModelName).Respond(
            [new ChatMessage(ChatRole.User, UserPrompt)]);

        Assert.Equal(ScriptedReply, response.Text);
        Assert.Equal(1, test.Chat().CallCount);
    }
}
