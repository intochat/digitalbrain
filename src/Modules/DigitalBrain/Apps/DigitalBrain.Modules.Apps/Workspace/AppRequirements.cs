using System.Globalization;
using System.Reflection;
using DigitalBrain.Core;
using Microsoft.CodeAnalysis.CSharp;

namespace DigitalBrain.Apps;

internal sealed class AppRequirements(ModuleInventory inventory)
{
    public void Check(PackageContent content)
    {
        var references = content.Programs().Concat(content.File(PackageContent.TestsPath) is { } tests
            ? [new KeyValuePair<string, string>(PackageContent.TestsPath, tests)] : [])
            .SelectMany(file => References(file.Key, file.Value)).Distinct(StringComparer.Ordinal).ToArray();
        if (references.Length == 0) { return; }
        var available = new HashSet<string>(StringComparer.Ordinal);
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            foreach (var assembly in inventory.ContractAssemblies()) { available.Add(assembly.GetName().Name!); }
        }
        catch (Exception error) when (error is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw new AppRequirementsException(Format(AppRequirementsException.UnresolvableReferences,
                error is FileNotFoundException missing ? missing.FileName ?? "composed contracts" : "composed contracts"));
        }
        var missingModules = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var reference in references)
        {
            if (available.Contains(reference)) { continue; }
            // These are the support projects supplied by the script environment, not optional modules.
            if (reference is "DigitalBrain.Client" or "DigitalBrain.Sdk") { continue; }
            const string prefix = "DigitalBrain.Modules.";
            const string suffix = ".Contracts";
            if (reference.StartsWith(prefix, StringComparison.Ordinal) && reference.EndsWith(suffix, StringComparison.Ordinal))
            { missingModules.Add(reference[prefix.Length..^suffix.Length]); }
            else { unresolved.Add(reference); }
        }
        var messages = new List<string>();
        if (missingModules.Count > 0) { messages.Add(Format(AppRequirementsException.MissingModules, string.Join(", ", missingModules))); }
        if (unresolved.Count > 0) { messages.Add(Format(AppRequirementsException.UnresolvableReferences, string.Join(", ", unresolved))); }
        if (messages.Count > 0) { throw new AppRequirementsException(string.Join(" ", messages)); }
    }

    private static IEnumerable<string> References(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        foreach (var trivia in tree.GetRoot().DescendantTrivia())
        {
            if (!trivia.IsDirective) { continue; }
            var text = trivia.ToString().Trim();
            if (!text.StartsWith("#:project", StringComparison.Ordinal)) { continue; }
            var argument = text[9..].Trim();
            if (argument.StartsWith('"') && argument.EndsWith('"') && argument.Length > 1)
            { argument = argument[1..^1]; }
            if (!argument.EndsWith(".csproj", StringComparison.Ordinal) || argument.Contains('"') || string.IsNullOrWhiteSpace(argument[..^7]))
            {
                var line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line + 1;
                throw new AppRequirementsException(Format(AppRequirementsException.InvalidDirective, $"{path}:{line}"));
            }
            yield return argument.Replace('\\', '/').Split('/')[^1][..^7];
        }
    }

    private static string Format(string template, string value) => string.Format(CultureInfo.InvariantCulture, template, value);
}
