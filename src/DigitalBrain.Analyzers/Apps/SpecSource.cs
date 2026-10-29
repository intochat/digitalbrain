using System;
using System.IO;
using System.Linq;
using System.Threading;
using DigitalBrain.Apps;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>Finds the spec comment of an app type with the shared <see cref="SpecComment"/> extractor.</summary>
internal static class SpecSource
{
    /// <summary>The spec text of <paramref name="type"/>: the leading comment of a declaring file or of the declaration,
    /// else a sibling <c>&lt;Type&gt;.spec.md</c> / <c>&lt;file&gt;.spec.md</c> AdditionalFile; null when there is none.</summary>
    public static string? Find(INamedTypeSymbol type, AnalyzerOptions options, CancellationToken cancellation)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var root = reference.SyntaxTree.GetRoot(cancellation);
            if (SpecComment.Extract(root.GetLeadingTrivia().ToFullString()) is { } fileSpec) { return fileSpec; }
            var declaration = reference.GetSyntax(cancellation);
            // On a declaration only a block comment counts; a // line there is ordinary commentary.
            var blocks = string.Concat(declaration.GetLeadingTrivia().Where(t => t.IsKind(SyntaxKind.MultiLineCommentTrivia)).Select(t => t.ToFullString()));
            if (SpecComment.Extract(blocks) is { } typeSpec) { return typeSpec; }
        }

        foreach (var file in options.AdditionalFiles)
        {
            if (!IsSpecFileOf(type, file.Path)) { continue; }
            var text = file.GetText(cancellation);
            return text is null ? string.Empty : Read(text);
        }
        return null;
    }

    private static string Read(SourceText text)
        => text.Length > SpecComment.MaxLength + 1 ? text.ToString(new TextSpan(0, SpecComment.MaxLength + 1)) : text.ToString();

    private static bool IsSpecFileOf(INamedTypeSymbol type, string path)
    {
        var name = Path.GetFileName(path);
        if (string.Equals(name, type.Name + ".spec.md", StringComparison.OrdinalIgnoreCase)) { return true; }
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            var source = reference.SyntaxTree.FilePath;
            if (string.IsNullOrEmpty(source)) { continue; }
            if (string.Equals(name, Path.GetFileNameWithoutExtension(source) + ".spec.md", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetDirectoryName(path), Path.GetDirectoryName(source), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
