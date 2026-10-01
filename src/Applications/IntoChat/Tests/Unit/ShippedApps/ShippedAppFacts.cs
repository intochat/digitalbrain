using DigitalBrain.Apps;

namespace IntoChat.Tests.Unit.ShippedApps;

// Package scenarios execute through their tests.cs publish gate.
public sealed class ShippedAppFacts
{
    private static readonly EmbeddedShippedAppSource Source = new(typeof(Program).Assembly, "IntoChat.ShippedApps/", "intochat");
    [Fact]
    public void EveryFirstPartyPackageShips()
        => Assert.Equal(["assistant", "customer-researcher", "group-chat", "settings", "word-count"], Source.Load().Select(app => app.Package.Name).Order());

    [Fact]
    public void EveryShippedProgramAndTestsFileParsesAsCSharp()
    {
        foreach (var app in Source.Load())
        {
            var sources = new Dictionary<string, string> { [PackageContent.TestsPath] = app.Content.File(PackageContent.TestsPath)! };
            foreach (var (path, program) in app.Content.Programs()) { sources[path] = program; }
            foreach (var (path, source) in sources)
            {
                // dotnet run strips the #: file-based-app directives before compiling.
                var stripped = string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("#:", StringComparison.Ordinal)));
                var errors = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree
                    .ParseText(stripped, cancellationToken: TestContext.Current.CancellationToken)
                    .GetDiagnostics(TestContext.Current.CancellationToken)
                    .Where(diagnostic => diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToArray();
                Assert.True(errors.Length == 0, $"{app.Package}/{path}: {string.Join("; ", errors.Take(3).Select(error => error.ToString()))}");
            }
        }
    }

    // The researcher is the proof that a first-party app is an ordinary package: every contract its
    // scripts compile against is a platform module a user's own app could name, so its tests verify
    // on a host where no CustomerResearcher module is composed.
    [Fact]
    public void TheResearcherPackageUsesOnlyPlatformContracts()
    {
        string[] platform =
        [
            "/brain/src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Contracts/DigitalBrain.Modules.Apps.Contracts.csproj",
            "/brain/src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Contracts/DigitalBrain.Modules.Flutter.Contracts.csproj",
            "/brain/src/Modules/Microsoft/Playwright/DigitalBrain.Modules.Microsoft.Playwright.Contracts/DigitalBrain.Modules.Microsoft.Playwright.Contracts.csproj",
            "/brain/src/Modules/AI/DigitalBrain.Modules.AI.Contracts/DigitalBrain.Modules.AI.Contracts.csproj",
            "/brain/src/Modules/Postgres/DigitalBrain.Modules.Postgres.Contracts/DigitalBrain.Modules.Postgres.Contracts.csproj",
        ];
        var researcher = Source.Load().Single(app => app.Package.Name == "customer-researcher");
        Assert.True(string.IsNullOrEmpty(researcher.Content.Source), "The researcher still carries a legacy app.cs.");
        Assert.Equal(["behaviors/research.cs", "behaviors/surface.cs"], researcher.Content.Programs().Keys);
        var sources = researcher.Content.Programs().Values.Append(researcher.Content.File(PackageContent.TestsPath)!);
        Assert.All(sources, source =>
        {
            Assert.DoesNotContain("CustomerResearcher.Contracts", source, StringComparison.Ordinal);
            foreach (var directive in source.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("#:project", StringComparison.Ordinal)))
            { Assert.Contains(directive["#:project".Length..].Trim(), platform); }
        });
    }

    [Fact]
    public void EveryShippedAppCarriesSpecAndTests()
        => Assert.All(Source.Load(), app =>
        {
            Assert.False(string.IsNullOrWhiteSpace(app.Content.File(PackageContent.SpecPath)), $"{app.Package} has no {PackageContent.SpecPath}.");
            Assert.False(string.IsNullOrWhiteSpace(app.Content.File(PackageContent.TestsPath)), $"{app.Package} has no {PackageContent.TestsPath}.");
        });

}
