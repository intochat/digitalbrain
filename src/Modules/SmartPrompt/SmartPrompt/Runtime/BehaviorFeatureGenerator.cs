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

        var response = await gemma.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System,
                    "DigitalBrain Behavior feature compiler. Return only one valid English Gherkin feature, "
                    + "without Markdown fences or commentary. Every production scenario must have @behavior and "
                    + "exactly one matching @test scenario. Use only the supplied Reqnroll step templates; replace "
                    + "template placeholders with concrete values. Do not invent steps.\nAvailable steps:\n" + steps),
                new ChatMessage(ChatRole.User, request.Trim()),
            ],
            cancellationToken: cancellationToken);

        var source = StripMarkdown(response.Text ?? "");
        return new BehaviorGeneration(source, compiler.Compile(source), ModelName);
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
