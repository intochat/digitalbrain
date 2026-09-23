using DigitalBrain.Apps;

namespace DigitalBrain.Marketplace;

// Golden-prompt precision: how many of an app's example prompts actually match one of its declared
// operations. A manifest whose examples do not describe its operations scores low and is rejected.
internal sealed class GoldenPromptEvaluator : IGoldenPromptEvaluator
{
    public Task<GoldenPromptReport> EvaluateAsync(AppManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.ExamplePrompts.Count == 0)
        {
            return Task.FromResult(new GoldenPromptReport { Total = 0, Accepted = 0 });
        }

        var operationVocabulary = manifest.Operations
            .SelectMany(operation => Tokens(operation.Name).Concat(Tokens(operation.DescriptionForModel)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var accepted = manifest.ExamplePrompts.Count(prompt => Tokens(prompt).Any(operationVocabulary.Contains));
        return Task.FromResult(new GoldenPromptReport { Total = manifest.ExamplePrompts.Count, Accepted = accepted });
    }

    private static IEnumerable<string> Tokens(string text) => text
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length >= 4);

    private static readonly char[] Separators =
        [' ', '\t', '\n', '\r', ',', '.', ':', ';', '!', '?', '(', ')', '[', ']', '"', '\'', '-', '_', '/', '\\'];
}
