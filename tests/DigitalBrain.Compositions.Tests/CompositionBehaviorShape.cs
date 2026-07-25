using System.Text.RegularExpressions;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class CompositionBehaviorShape
{
    private static readonly string CompositionsRoot = LocateCompositionsRoot();

    private static readonly Regex PublicTypeDeclaration = new(
        @"\bpublic\s+(?:sealed\s+|abstract\s+|static\s+|partial\s+)*(?:class|record|struct|interface)\s+(\w+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact(DisplayName =
        "each pre-rail composition file is one public sealed class (future Behavior identity)")]
    public void EachCompositionFileIsOnePublicSealedClass()
    {
        var sources = EnumerateCompositionSources().ToArray();
        Assert.NotEmpty(sources);

        foreach (var sourcePath in sources)
        {
            var text = File.ReadAllText(sourcePath);
            var publicTypes = PublicTypeDeclaration.Matches(text)
                .Select(match => match.Groups[1].Value)
                .ToArray();

            Assert.True(
                publicTypes.Length == 1,
                $"{Relative(sourcePath)} must declare exactly one public type; found: " +
                (publicTypes.Length == 0 ? "<none>" : string.Join(", ", publicTypes)));

            Assert.Contains(
                "public sealed class " + publicTypes[0],
                text,
                StringComparison.Ordinal);
        }
    }

    [Fact(DisplayName =
        "composition bodies never construct peer compositions — only IDigitalBrain + contracts")]
    public void CompositionBodiesNeverConstructPeerCompositions()
    {
        var sources = EnumerateCompositionSources()
            .Select(path => (
                Path: path,
                TypeName: PublicTypeName(File.ReadAllText(path)),
                Text: File.ReadAllText(path)))
            .Where(source => source.TypeName is not null)
            .Select(source => (source.Path, TypeName: source.TypeName!, source.Text))
            .ToArray();

        Assert.NotEmpty(sources);

        foreach (var source in sources)
        {
            foreach (var peer in sources)
            {
                if (source.TypeName == peer.TypeName)
                {
                    continue;
                }

                var construction = "new " + peer.TypeName + "(";
                Assert.DoesNotContain(
                    construction,
                    source.Text,
                    StringComparison.Ordinal);
            }
        }
    }

    private static string? PublicTypeName(string text)
    {
        var match = PublicTypeDeclaration.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IEnumerable<string> EnumerateCompositionSources()
        => Directory.EnumerateFiles(CompositionsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path));

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string Relative(string path)
        => Path.GetRelativePath(CompositionsRoot, path);

    private static string LocateCompositionsRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "samples",
                "DigitalBrain.Compositions");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            "Could not locate samples/DigitalBrain.Compositions from the test output directory.");
    }
}
