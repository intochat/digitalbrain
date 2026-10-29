using System.Text.RegularExpressions;

namespace DigitalBrain.Architecture.Tests.Unit;

// DESIGN section 1: Orleans namespaces and serializer attributes appear only in the runtime packages (and tests).
public sealed partial class OrleansAttributeOwnerFacts
{
    [GeneratedRegex(@"^\s*using\s+(static\s+)?Orleans(\.|;)|\[\s*(GenerateSerializer|Alias|Id)\s*[\(\]]", RegexOptions.Multiline)]
    private static partial Regex OrleansUse();

    [Fact]
    public void Orleans_appears_only_under_the_runtime()
    {
        var src = Path.Combine(Repository.Root, "src");
        var files = Repository.SourceFiles(src, "*.cs").ToList();
        Assert.NotEmpty(files);

        var runtimeFiles = files.Where(IsRuntime).ToList();
        Assert.Contains(runtimeFiles, file => OrleansUse().IsMatch(File.ReadAllText(file)));

        var hits = files.Where(file => !IsRuntime(file) && OrleansUse().IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(Repository.Root, file))
            .ToList();
        Assert.True(hits.Count == 0, string.Join(Environment.NewLine, hits));

        bool IsRuntime(string file)
            => Path.GetRelativePath(src, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0]
                .StartsWith("DigitalBrain.Runtime.Orleans", StringComparison.Ordinal);
    }
}
