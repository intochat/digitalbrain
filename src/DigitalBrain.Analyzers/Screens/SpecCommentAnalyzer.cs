using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Screens;

/// <summary>
/// DB0027: a screen type has a spec comment: a leading <c>/* ... */</c> comment in a file that declares it, or an
/// AdditionalFiles item named <c>&lt;Type&gt;.spec.md</c> (or <c>&lt;file&gt;.spec.md</c> next to a script <c>screen.cs</c>).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SpecCommentAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(ScreenRules.MissingSpec);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(Analyze, SymbolKind.NamedType);
    }

    private static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (!ScreenRules.IsScreen(type) || type.IsAbstract) { return; }

        if (SpecSource.Find(type, context.Options, context.CancellationToken) is not null) { return; }

        context.ReportDiagnostic(Diagnostic.Create(ScreenRules.MissingSpec, type.Locations[0], type.Name));
    }
}
