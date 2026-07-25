using DigitalBrain.AI.Ollama;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class AISmoke(ModuleFixture fixture)
{
    [Fact(DisplayName = "typed LLM returns the scripted edge response")]
    public async Task TypedLlmReturnsTheScriptedResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        test.Chat().Reply("typed response");

        var response = await test.Client.Get<ILlama32>("typed-model").Respond(
            [new ChatMessage(ChatRole.User, "hello")]);

        Assert.Equal("typed response", response.Text);
        Assert.Single(test.Chat().Calls);
    }
}
