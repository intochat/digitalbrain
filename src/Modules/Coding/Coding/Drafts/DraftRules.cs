using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Coding;

internal static class DraftRules
{
    public const int MaximumSourceBytes = 128 * 1024;

    public static void Validate(SaveCodeDraft request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.ExpectedRevision);
        if (request.OperationId == Guid.Empty) { throw new ArgumentException("Operation ID is required.", nameof(request)); }
        Source(request.Source);
        Source(request.Tests);
        ArgumentNullException.ThrowIfNull(request.ModuleIds);
        if (request.ModuleIds.Count > 32 || request.ModuleIds.Distinct(StringComparer.Ordinal).Count() != request.ModuleIds.Count)
        { throw new ArgumentException("Use at most 32 distinct module IDs.", nameof(request)); }
        foreach (var id in request.ModuleIds)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.'))
            { throw new ArgumentException("Invalid installed module ID.", nameof(request)); }
        }
    }

    private static void Source(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (Encoding.UTF8.GetByteCount(source) > MaximumSourceBytes)
        { throw new ArgumentException("Source must be at most 128 KiB of UTF-8.", nameof(source)); }
        var root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsDirective && trivia.ToFullString().TrimStart().StartsWith("#:", StringComparison.Ordinal))
            { throw new ArgumentException("Managed drafts cannot contain file-app build directives.", nameof(source)); }
        }
    }
}
