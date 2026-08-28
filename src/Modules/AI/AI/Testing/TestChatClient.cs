using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

// Deterministic, offline stand-in for a real LLM (AITestingClients). Scripts exactly two
// tool-calling scenarios the kit UC1/UC2 E2E suite depends on -- render_chart and
// generate_image -- keyed off the latest user message; everything else keeps the original
// fixed reply so every other testing-mode caller (MCP, BDD, simulation) is unaffected.
internal sealed partial class TestChatClient : IChatClient
{
    private const string Reply = "Test assistant reply.";
    private const string RenderedReply = "Rendered.";
    private const string GeneratedReply = "Generated.";
    private const string RenderChartToolName = "render_chart";
    private const string GenerateImageToolName = "generate_image";
    private const string GenerateBehaviorToolName = "generate_behavior_feature";
    private const string RunBehaviorToolName = "run_behavior_example";
    private const string RunSalesforceEnrichmentToolName = "run_salesforce_account_enrichment";
    private const string LearnExperienceToolName = "learn_experience";
    private const string FallbackChatName = "main";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        var conversation = messages.ToList();

        // A FunctionResultContent in the history means FunctionInvokingChatClient already ran
        // the tool this client asked for on the prior round; reply per whichever tool that was
        // (its FunctionCallContent is still in the same history, appended by that middleware).
        if (conversation.Any(static m => m.Contents.OfType<FunctionResultContent>().Any()))
        {
            var priorCall = conversation
                .SelectMany(static m => m.Contents.OfType<FunctionCallContent>())
                .FirstOrDefault();
            var reply = string.Equals(priorCall?.Name, GenerateImageToolName, StringComparison.Ordinal)
                ? GeneratedReply
                : priorCall?.Name is GenerateBehaviorToolName or RunBehaviorToolName
                    or RunSalesforceEnrichmentToolName or LearnExperienceToolName
                    ? "Experience ready."
                    : RenderedReply;
            yield return new ChatResponseUpdate(ChatRole.Assistant, reply) { FinishReason = ChatFinishReason.Stop };
            yield break;
        }

        var lastUser = conversation.LastOrDefault(static m => m.Role == ChatRole.User)?.Text ?? "";
        var tools = options?.Tools?.OfType<AIFunction>().ToList() ?? [];

        var learnExperience = tools.FirstOrDefault(static tool => tool.Name == LearnExperienceToolName);
        if (learnExperience is not null
            && lastUser.Contains("preserve", StringComparison.OrdinalIgnoreCase)
            && lastUser.Contains("verified", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", LearnExperienceToolName, new Dictionary<string, object?>
            {
                ["name"] = "salesforce-account-enrichment",
                ["request"] = lastUser,
                ["evidence"] = lastUser,
            })])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        var runEnrichment = tools.FirstOrDefault(static tool => tool.Name == RunSalesforceEnrichmentToolName);
        if (runEnrichment is not null
            && lastUser.Contains("enrich", StringComparison.OrdinalIgnoreCase)
            && (lastUser.Contains("salesforce", StringComparison.OrdinalIgnoreCase)
                || lastUser.Contains("company", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", RunSalesforceEnrichmentToolName, new Dictionary<string, object?>
            {
                ["email"] = lastUser.Contains("vlad@intochat.io", StringComparison.OrdinalIgnoreCase)
                    ? "vlad@intochat.io"
                    : "vlad@intochat.io",
            })])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        if (conversation.Any(static message => message.Role == ChatRole.System
            && message.Text.Contains("DigitalBrain Behavior feature compiler", StringComparison.Ordinal)))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, GeneratedBehaviorFeature)
            {
                FinishReason = ChatFinishReason.Stop,
            };
            yield break;
        }

        var runBehavior = tools.FirstOrDefault(static tool => tool.Name == RunBehaviorToolName);
        if (runBehavior is not null
            && lastUser.Contains("behavior", StringComparison.OrdinalIgnoreCase)
            && lastUser.Contains("run", StringComparison.OrdinalIgnoreCase))
        {
            var name = lastUser.Contains("bitcoin", StringComparison.OrdinalIgnoreCase)
                ? "bitcoin-tracker"
                : "urgent-email";
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", RunBehaviorToolName, new Dictionary<string, object?> { ["name"] = name })])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        var generateBehavior = tools.FirstOrDefault(static tool => tool.Name == GenerateBehaviorToolName);
        if (generateBehavior is not null
            && lastUser.Contains("behavior", StringComparison.OrdinalIgnoreCase)
            && lastUser.Contains("create", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [new FunctionCallContent("call-1", GenerateBehaviorToolName, new Dictionary<string, object?>
            {
                ["name"] = "generated-bitcoin-alert",
                ["request"] = lastUser,
            })])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        // Precedence: "chart" beats "image" when a message somehow mentions both.
        var renderChart = tools.FirstOrDefault(static tool => tool.Name == RenderChartToolName);
        if (renderChart is not null && lastUser.Contains("chart", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", RenderChartToolName, new Dictionary<string, object?>
                {
                    ["chatName"] = ChatNameFromContext(conversation),
                    ["title"] = "Test chart",
                    ["chartKind"] = "bar",
                    ["labels"] = new[] { "A", "B" },
                    ["values"] = new[] { 1.0, 2.0 },
                }),
            ])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        var generateImage = tools.FirstOrDefault(static tool => tool.Name == GenerateImageToolName);
        if (generateImage is not null && lastUser.Contains("image", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant,
            [
                new FunctionCallContent("call-1", GenerateImageToolName, new Dictionary<string, object?>
                {
                    ["chatName"] = ChatNameFromContext(conversation),
                    ["prompt"] = "Test image",
                }),
            ])
            { FinishReason = ChatFinishReason.ToolCalls };
            yield break;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, Reply) { FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    // ChatTurnWorker's conversation-context line quotes the chat's full grain key right after
    // the word "chat" -- keep this pattern in sync with that literal. Scans every message
    // rather than trusting "the first system message" to be it: Agent.RespondStreaming
    // prepends the assistant's OWN persona instructions as a system message ahead of
    // ChatTurnWorker's conversation-context one, so index 0 is the wrong message here.
    // Returning "main" when the pattern is absent from all of them is a loud failure
    // downstream (no such chat), never a silent wrong match.
    private static string ChatNameFromContext(IReadOnlyList<ChatMessage> conversation)
    {
        foreach (var message in conversation)
        {
            var match = ChatNameInContext().Match(message.Text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return FallbackChatName;
    }

    [GeneratedRegex("chat '([^']+)'")]
    private static partial Regex ChatNameInContext();

    private const string GeneratedBehaviorFeature =
        """
        Feature: Generated Bitcoin alert
          @behavior
          Scenario: Notify on a high Bitcoin price
            Given Market.Symbol("BTCUSD")
            When Market.Price changes
            And the event value is above 90000
            Then notify UI.Chat("main")
          @test
          Scenario: A high Bitcoin price notifies the chat
            Given fake event "market.price" from "BTCUSD" with text "BTC breakout" and value 95000
            When behavior "Notify on a high Bitcoin price" runs
            Then UI.Chat("main") contains a behavior notification
        """;
}
