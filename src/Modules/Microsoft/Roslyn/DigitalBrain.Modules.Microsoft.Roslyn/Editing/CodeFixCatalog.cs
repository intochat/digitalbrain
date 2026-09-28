using System.Reflection;
using Microsoft.CodeAnalysis.CodeFixes;

namespace DigitalBrain.Microsoft.Roslyn;

// The C# code fixers ship as MEF exports in Microsoft.CodeAnalysis.CSharp.Features. This host has no MEF
// composition for them, so the ones with a parameterless constructor are built by reflection; fixers that
// import services are skipped, and a fixer that throws while registering is skipped by the editor.
public sealed class CodeFixCatalog
{
    private readonly Lazy<IReadOnlyList<CodeFixProvider>> _providers = new(Discover);

    public IReadOnlyList<CodeFixProvider> Providers => _providers.Value;

    public IEnumerable<CodeFixProvider> For(string diagnosticId)
        => Providers.Where(provider => provider.FixableDiagnosticIds.Contains(diagnosticId, StringComparer.Ordinal));

    private static IReadOnlyList<CodeFixProvider> Discover()
    {
        var assembly = Assembly.Load("Microsoft.CodeAnalysis.CSharp.Features");
        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            types = partial.Types;
        }

        var providers = new List<CodeFixProvider>();
        foreach (var type in types)
        {
            if (type is null || type.IsAbstract || !typeof(CodeFixProvider).IsAssignableFrom(type)
                || type.GetCustomAttribute<ExportCodeFixProviderAttribute>() is null)
            {
                continue;
            }

            var constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, Type.EmptyTypes);
            if (constructor is null)
            {
                continue;
            }

            try
            {
                providers.Add((CodeFixProvider)constructor.Invoke(null));
            }
            catch (TargetInvocationException)
            {
                // A fixer whose constructor needs the IDE host is not usable here.
            }
        }

        return providers;
    }
}