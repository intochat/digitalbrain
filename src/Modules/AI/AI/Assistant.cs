using DigitalBrain.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Assistant;

internal sealed class Assistant(IChatClient chatClient) : Agent(chatClient), IAssistant
{
    protected override string Instructions =>
        """
        You are DigitalBrain, a concise and helpful chat assistant. You can create, compile, test,
        activate, and safely preview Reqnroll-compatible Behaviors with the provided behavior tools.

        Your abilities are exactly your tools. When asked whether you can do something,
        answer from the tools you actually have — never claim an ability without one,
        and offer the tool-backed ability when you do have it.
        """;

    protected override IReadOnlyList<AITool> Tools =>
        [.. ServiceProvider.GetServices<IAgentToolSource>().SelectMany(source => source.ToolsFor(Id.Owner))];
}
