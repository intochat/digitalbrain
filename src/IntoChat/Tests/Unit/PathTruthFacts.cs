using System.Xml.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IntoChat.Tests.Unit;

public sealed class PathTruthFacts
{
    [Fact]
    public void AppHostConfiguresCapacityWithoutContent()
    {
        var ct = TestContext.Current.CancellationToken;
        var root = CSharpSyntaxTree.ParseText(Read("src/IntoChat/AppHost/AppHost.cs"), cancellationToken: ct).GetRoot(ct);
        var modules = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(call => call.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.Text: "WithModule" } });
        var permittedCalls = new HashSet<string>(StringComparer.Ordinal)
        {
            "WithLlm", "WithDefaultLlm", "WithDefaultEmbedding", "WithVoiceToText", "WithTavilySearch",
            "WithHostedQdrant", "WithClickHouse", "WithConnection", "WithPostgres", "WithGmail", "WithHostedMcp", "RunDesktopApp"
        };
        foreach (var module in modules)
        {
            foreach (var call in module.ArgumentList.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var member = Assert.IsType<MemberAccessExpressionSyntax>(call.Expression);
                Assert.Contains(member.Name.Identifier.ValueText, permittedCalls);
                if (member.Name.Identifier.ValueText == "WithClickHouse") { Assert.Empty(call.ArgumentList.Arguments); }
            }
            foreach (var literal in module.ArgumentList.DescendantNodes().OfType<LiteralExpressionSyntax>().Where(value => value.IsKind(SyntaxKind.StringLiteralExpression)))
            { Assert.Contains(literal.Token.ValueText, new[] { "supabase", "digitalbrain" }); }
            foreach (var assignment in module.ArgumentList.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            { Assert.Contains(assignment.Left.ToString(), new[] { "options.DatabaseName", "ai.Telemetry.EnableSensitiveData" }); }
        }
    }

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException($"Repository root (DigitalBrain.slnx) was not found above {AppContext.BaseDirectory}.");
    }

    private static string PathInRepo(string relative) => Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string Read(string relative) => File.ReadAllText(PathInRepo(relative));

    [Fact]
    public void TheBrowserModuleSelectedByTheAppHostShipsWithTheRuntime()
    {
        using var dependencies = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "IntoChat.deps.json")));
        Assert.Contains(dependencies.RootElement.GetProperty("libraries").EnumerateObject(),
            library => library.Name.StartsWith("DigitalBrain.Modules.Microsoft.Playwright/", StringComparison.Ordinal));
    }

    [Fact]
    public void ContainerModuleListsAgreeAndResolve()
    {
        const string prefix = "DigitalBrain__Modules__";
        var docker = Regex.Matches(Read("src/IntoChat/IntoChat/Dockerfile"), "DigitalBrain__Modules__([a-z-]+)__Enabled=\"true\"")
            .Select(match => match.Groups[1].Value).Order().ToArray();
        var profile = XDocument.Load(PathInRepo("src/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml"))
            .Descendants("ContainerEnvironmentVariable")
            .Where(entry => entry.Attribute("Include")!.Value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry =>
            {
                Assert.Equal("true", entry.Attribute("Value")!.Value);
                return entry.Attribute("Include")!.Value[prefix.Length..^"__Enabled".Length];
            }).Order().ToArray();
        Assert.NotEmpty(docker);
        Assert.Equal(docker.Length, docker.Distinct().Count());
        Assert.Equal(docker, profile);
        var registered = Regex.Matches(Read("src/IntoChat/IntoChat/Program.cs"), "AddModule<[^>]+>\\(\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value).Order().ToArray();
        Assert.Equal(docker, registered);
    }
}
