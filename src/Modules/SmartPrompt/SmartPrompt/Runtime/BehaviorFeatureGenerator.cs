using System.Text;
using DigitalBrain.AI.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.SmartPrompt;

internal sealed class BehaviorFeatureGenerator(
    [FromKeyedServices(typeof(IGemma4))] IChatClient gemma,
    IBehaviorCompiler compiler) : IBehaviorFeatureGenerator
{
    private const string ModelName = "gemma4:e2b";

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
        var reference = BehaviorFeatureFallback.BestMatch(request).Source;

        var conversation = new List<ChatMessage>
        {
            new(ChatRole.System,
                "DigitalBrain Behavior feature compiler. Return only one valid English Gherkin feature, "
                + "without Markdown fences or commentary. Every production scenario must have @behavior and "
                + "exactly one matching @test scenario. Use only the supplied Reqnroll step templates; replace "
                + "template placeholders with concrete values. Do not invent steps. Every tag must be followed by "
                + "a Scenario: line. The @test must invoke the exact @behavior scenario name. Preserve the complete "
                + "Feature/tag/Scenario/Given/When/Then structure shown in the reference.\nAvailable steps:\n"
                + steps + "\nReference feature shape:\n" + reference),
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
                "Correct the feature and return the full source only. Do not remove either Scenario: line, either "
                + "tag, or the paired fake/invoke/assert test. Compiler diagnostics:\n"
                + string.Join("\n", compilation.Diagnostics.Select(diagnostic =>
                    $"line {diagnostic.Line}: {diagnostic.Message}"))));
        }
        if (!compilation.Success)
        {
            source = BehaviorFeatureFallback.FromRequest(request);
            compilation = compiler.Compile(source);
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
