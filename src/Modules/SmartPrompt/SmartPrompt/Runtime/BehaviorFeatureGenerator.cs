using System.Text;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.SmartPrompt;

internal sealed class BehaviorFeatureGenerator(
    [FromKeyedServices(typeof(IGemma4))] IChatClient gemma,
    IBehaviorCompiler compiler) : IBehaviorFeatureGenerator
{
    private const string ModelName = "gemma4:12b";

    public async Task<BehaviorGeneration> Generate(
        string request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var steps = new StringBuilder();
        foreach (var suggestion in compiler.Suggestions)
        {
            steps.Append(suggestion.Keyword).Append(' ').AppendLine(suggestion.Template);
        }

        var conversation = new List<ChatMessage>
        {
            new(ChatRole.System,
                "DigitalBrain Behavior feature compiler. Return only one valid English Gherkin feature, "
                + "without Markdown fences or commentary. Every production scenario must have @behavior and "
                + "exactly one matching @test scenario. Use only the supplied Reqnroll step templates; replace "
                + "template placeholders with concrete values. Do not invent steps.\nAvailable steps:\n" + steps),
            new(ChatRole.User, request.Trim()),
        };

        var source = "";
        var compilation = compiler.Compile(source);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await gemma.GetResponseAsync(conversation, cancellationToken: cancellationToken);
            source = StripMarkdown(response.Text ?? "");
            compilation = compiler.Compile(source);
            if (compilation.Success)
            {
                break;
            }
            conversation.Add(new ChatMessage(ChatRole.Assistant, source));
            conversation.Add(new ChatMessage(ChatRole.User,
                "Correct the feature and return the full source only. Compiler diagnostics:\n"
                + string.Join("\n", compilation.Diagnostics.Select(diagnostic =>
                    $"line {diagnostic.Line}: {diagnostic.Message}"))));
        }
        return new BehaviorGeneration(source, compilation, ModelName);
    }

    private static string StripMarkdown(string value)
    {
        var result = value.Trim();
        if (!result.StartsWith("```", StringComparison.Ordinal))
        {
            return result;
        }
        var firstNewline = result.IndexOf('\n');
        var lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewline >= 0 && lastFence > firstNewline
            ? result[(firstNewline + 1)..lastFence].Trim()
            : result;
    }
}
