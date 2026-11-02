using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers.Apps;

/// <summary>
/// DB0030: an application assembly must define exactly one class decorated with [App] and inheriting from DigitalBrain.App.
/// Excludes test projects.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class SingleAppClassAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(AppRules.SingleAppClass);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(AnalyzeCompilation);
    }

    private static void AnalyzeCompilation(CompilationAnalysisContext context)
    {
        if (TestProjectExclusion.IsTestProject(context.Compilation, context.Options))
        {
            return;
        }

        if (context.Options != null &&
            context.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.DigitalBrainApp", out var isApp) &&
            bool.TryParse(isApp.Trim(), out var b) && !b)
        {
            return;
        }

        var appClasses = new List<INamedTypeSymbol>();
        var hasModule = false;
        CollectTypes(context.Compilation.GlobalNamespace, appClasses, ref hasModule);

        if (hasModule)
        {
            return;
        }

        if (appClasses.Count > 1)
        {
            var names = string.Join(", ", appClasses.Select(c => c.Name).ToArray());
            foreach (var appClass in appClasses)
            {
                var loc = appClass.Locations.Length > 0 ? appClass.Locations[0] : Location.None;
                context.ReportDiagnostic(Diagnostic.Create(AppRules.SingleAppClass, loc, appClasses.Count, names));
            }
        }
        else if (appClasses.Count == 1)
        {
            var appClass = appClasses[0];
            if (appClass.IsAbstract || appClass.IsGenericType)
            {
                var loc = appClass.Locations.Length > 0 ? appClass.Locations[0] : Location.None;
                context.ReportDiagnostic(Diagnostic.Create(AppRules.SingleAppClass, loc, 1, appClass.Name));
            }
        }
    }

    private static void CollectTypes(INamespaceOrTypeSymbol container, List<INamedTypeSymbol> appClasses, ref bool hasModule)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol childNs)
            {
                CollectTypes(childNs, appClasses, ref hasModule);
            }
            else if (member is INamedTypeSymbol type)
            {
                if (Rules.Has(type, Rules.ModuleAttribute) || Rules.DerivesFrom(type, Rules.ModuleType))
                {
                    hasModule = true;
                }
                if (Rules.Has(type, Rules.AppAttribute))
                {
                    appClasses.Add(type);
                }
                CollectTypes(type, appClasses, ref hasModule);
            }
        }
    }
}
