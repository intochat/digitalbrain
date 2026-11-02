using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DigitalBrain.Analyzers;

internal static class TestProjectExclusion
{
    public static bool IsTestProject(Compilation compilation, AnalyzerOptions? options = null)
    {
        if (options != null && options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue("build_property.IsTestProject", out var isTest)
            && bool.TryParse(isTest, out var b) && b)
        {
            return true;
        }

        var assemblyName = compilation.AssemblyName;
        if (assemblyName != null)
        {
            if (assemblyName.IndexOf(".Tests", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assemblyName.IndexOf(".Test.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assemblyName.EndsWith(".Test", StringComparison.OrdinalIgnoreCase) ||
                assemblyName.EndsWith("Tests", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var reference in compilation.ReferencedAssemblyNames)
        {
            var name = reference.Name;
            if (name.StartsWith("xunit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("nunit", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.VisualStudio.TestPlatform", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase) ||
                name.IndexOf("TestingPlatform", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsInTestScope(INamedTypeSymbol type, Compilation compilation, AnalyzerOptions? options = null)
    {
        if (IsTestProject(compilation, options)) { return true; }
        var ns = type.ContainingNamespace != null ? type.ContainingNamespace.ToDisplayString() : string.Empty;
        return ns.IndexOf(".Tests", StringComparison.OrdinalIgnoreCase) >= 0 ||
               ns.IndexOf(".Test.", StringComparison.OrdinalIgnoreCase) >= 0 ||
               ns.EndsWith("Tests", StringComparison.OrdinalIgnoreCase);
    }
}
