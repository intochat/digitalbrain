using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class GherkinArchitecture
{
    private static readonly string RepositoryRoot = LocateRepositoryRoot();

    private static readonly string[] ForbiddenBindingTokens =
    [
        "IGrain" + "Factory",
        "Grain" + "Id",
        "Simulation" + "Cluster",
        "App" + "Domain",
        "Assembly." + "GetTypes",
        "Neuron" + "Catalog",
        "Activator." + "CreateInstance",
    ];

    [Fact]
    public void BindingsUseGeneratedCompiledVocabularyWithoutRuntimeCatalogs()
    {
        var bindingRoots = new[]
        {
            Path.Combine(
                RepositoryRoot,
                "src",
                "DigitalBrain.Testing",
                "Gherkin"),
            Path.Combine(
                RepositoryRoot,
                "tests",
                "DigitalBrain.ModuleTests",
                "Features"),
        };

        var violations = bindingRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(
                root,
                "*.cs",
                SearchOption.AllDirectories))
            .SelectMany(file => ForbiddenBindingTokens
                .Where(token => File.ReadAllText(file)
                    .Contains(token, StringComparison.Ordinal))
                .Select(token =>
                    $"{Path.GetRelativePath(RepositoryRoot, file)}: {token}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
        Assert.NotNull(typeof(GherkinArchitecture).Assembly.GetType(
            "DigitalBrain.Generated.GeneratedTestVocabulary",
            throwOnError: false));
    }

    private static string LocateRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Combine(
                directory.FullName,
                "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "DigitalBrain.slnx was not found above the test assembly.");
    }
}
