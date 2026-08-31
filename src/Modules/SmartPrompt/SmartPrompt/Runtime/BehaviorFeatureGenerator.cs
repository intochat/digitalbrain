using System.Text;
using Microsoft.Extensions.AI;

namespace DigitalBrain.SmartPrompt;

internal sealed class BehaviorFeatureGenerator(
    IChatClient chatClient,
    IBehaviorCompiler compiler) : IBehaviorFeatureGenerator
{
    private const string ModelName = "configured-default";

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
            var response = await GetResponseOrNull(conversation, cancellationToken);
            if (response is null)
            {
                break;
            }
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

    public async Task<BehaviorGeneration> GenerateCorrection(
        string activeSource,
        string request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(request);
        var parentCompilation = compiler.Compile(activeSource);
        if (!parentCompilation.Success || parentCompilation.Plan is null)
        {
            throw new InvalidOperationException("The active Experience cannot be corrected because it does not compile.");
        }

        var steps = new StringBuilder();
        foreach (var suggestion in compiler.Suggestions)
        {
            steps.Append(suggestion.Keyword).Append(' ').AppendLine(suggestion.Template);
        }
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.System,
                "DigitalBrain Experience correction compiler. Return only the complete corrected English Gherkin "
                + "feature without Markdown. This is a constrained edit: retain every existing @behavior scenario "
                + "name and trigger, retain every existing @test scenario and all its steps unchanged, and add at "
                + "least one new @test regression that fails on the active feature but passes on the correction. "
                + "Use only supplied step templates and do not invent steps.\nAvailable steps:\n" + steps
                + "\nActive feature to edit:\n" + activeSource),
            new(ChatRole.User, request.Trim()),
        };

        var source = "";
        var compilation = compiler.Compile(source);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await GetResponseOrNull(conversation, cancellationToken);
            if (response is null)
            {
                break;
            }
            source = StripMarkdown(response.Text ?? "");
            compilation = compiler.Compile(source);
            if (IsValidCorrection(compilation, parentCompilation.Plan))
            {
                break;
            }
            conversation.Add(new ChatMessage(ChatRole.Assistant, source));
            conversation.Add(new ChatMessage(ChatRole.User,
                "That is not a constrained red/green correction. Return the complete active feature with every "
                + "existing scenario/test retained and one genuinely new regression test."));
        }

        if (!IsValidCorrection(compilation, parentCompilation.Plan))
        {
            source = BehaviorFeatureFallback.ApplyCorrection(activeSource, request);
            compilation = compiler.Compile(source);
        }
        return new BehaviorGeneration(source, compilation, ModelName);
    }

    private async Task<ChatResponse?> GetResponseOrNull(
        IReadOnlyList<ChatMessage> conversation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await chatClient.GetResponseAsync(conversation, cancellationToken: cancellationToken);
        }
        catch (Exception failure) when (failure is HttpRequestException or TimeoutException
            || failure is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool IsValidCorrection(BehaviorCompilation compilation, BehaviorPlan parent)
    {
        if (!compilation.Success || compilation.Plan is null
            || !BehaviorTestInterpreter.Validate(compilation.Plan, compilation.Diagnostics).AllGreen)
        {
            return false;
        }
        var validation = BehaviorTestInterpreter.ValidateCorrectionCandidate(compilation.Plan, parent);
        return validation.StructurallyValid && !validation.ParentReport.AllGreen;
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
