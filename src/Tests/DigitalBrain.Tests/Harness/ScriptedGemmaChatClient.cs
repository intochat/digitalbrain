using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DigitalBrain.AI;
using DigitalBrain.Abstractions;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Tests.Harness;

internal sealed partial class ScriptedGemmaChatClient : IChatClient
{
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        await Task.CompletedTask;

        var turn = messages.ToArray();
        if (turn.Any(static message => message.Role == ChatRole.Tool))
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, "wired");
            yield break;
        }

        var prompt = LatestUserText(turn);
        var tools = options?.Tools?.OfType<AIFunction>().ToArray() ?? [];

        if (prompt.Contains("which tools", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                tools.Length == 0 ? "no tools offered" : string.Join(", ", tools.Select(static tool => tool.Name)));
            yield break;
        }

        var connect = tools.FirstOrDefault(
            static tool => tool.Name == ValidatedCapability.ToolNameFor("db.connect", 1));
        if (prompt.Contains("connect", StringComparison.OrdinalIgnoreCase) && connect is not null
            && OwnerFrom(turn) is { } owner)
        {
            var arguments = new Dictionary<string, object?>
            {
                ["connectionId"] = Guid.NewGuid(),
                ["source"] = new NeuronId("probesource", owner, "elon"),
                ["synapseAlias"] = "probe.fact",
                ["target"] = new NeuronId("chart", owner, "dashboard"),
                ["transform"] = ProbeFactToChartPoint.TransformName,
            };

            yield return new ChatResponseUpdate(
                ChatRole.Assistant,
                [new FunctionCallContent("wire-1", connect.Name, arguments)]);
            yield break;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, "ok");
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static string LatestUserText(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role == ChatRole.User)
            {
                return messages[index].Text;
            }
        }

        return string.Empty;
    }

    private static OwnerId? OwnerFrom(IReadOnlyList<ChatMessage> messages)
    {
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.System
                && OwnerLine().Match(message.Text) is { Success: true } declared)
            {
                return new OwnerId(declared.Groups[1].Value);
            }
        }

        return null;
    }

    [GeneratedRegex("belongs to owner '([^']+)'")]
    private static partial Regex OwnerLine();
}
