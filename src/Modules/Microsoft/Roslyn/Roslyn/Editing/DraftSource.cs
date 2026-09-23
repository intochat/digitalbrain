using System.Text;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Microsoft.Roslyn;

public static class DraftSource
{
    public static void RejectFileAppDirectives(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)).GetRoot();
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.IsDirective && trivia.ToFullString().TrimStart().StartsWith("#:", StringComparison.Ordinal))
            {
                throw new ArgumentException("Managed drafts cannot contain file-app build directives.", nameof(source));
            }
        }
    }
}