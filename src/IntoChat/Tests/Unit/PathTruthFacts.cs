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
        var docker = Read("src/IntoChat/IntoChat/Dockerfile").Split('\n')
            .Where(line => line.Contains(prefix, StringComparison.Ordinal))
            .Select(line =>
            {
                var assignment = line.Trim().TrimEnd('\\').Trim();
                if (assignment.StartsWith("ENV ", StringComparison.Ordinal)) { assignment = assignment[4..].Trim(); }
                var match = Regex.Match(assignment, "^DigitalBrain__Modules__(\\d+)=\"([^\"]*)\"$");
                Assert.True(match.Success, $"Malformed module assignment: {line}");
                Assert.NotEmpty(match.Groups[2].Value);
                return (Index: int.Parse(match.Groups[1].Value), Module: match.Groups[2].Value);
            }).ToArray();
        var profile = XDocument.Load(PathInRepo("src/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml"))
            .Descendants("ContainerEnvironmentVariable")
            .Where(entry => entry.Attribute("Include")!.Value.StartsWith(prefix, StringComparison.Ordinal))
            .Select(entry => (Index: int.Parse(entry.Attribute("Include")!.Value[prefix.Length..]),
                Module: entry.Attribute("Value")!.Value)).ToArray();

        Assert.NotEmpty(docker);
        Assert.NotEmpty(profile);
        Assert.Equal(docker.Length, docker.Select(entry => entry.Index).Distinct().Count());
        Assert.Equal(profile.Length, profile.Select(entry => entry.Index).Distinct().Count());
        Assert.Equal(Enumerable.Range(0, docker.Length), docker.Select(entry => entry.Index).Order());
        Assert.Equal(Enumerable.Range(0, profile.Length), profile.Select(entry => entry.Index).Order());
        Assert.Equal(docker.Length, docker.Select(entry => entry.Module).Distinct().Count());
        Assert.Equal(profile.Length, profile.Select(entry => entry.Module).Distinct().Count());
        Assert.Equal(docker.Select(entry => entry.Module).Order(StringComparer.Ordinal),
            profile.Select(entry => entry.Module).Order(StringComparer.Ordinal));
        var appHost = Read("src/IntoChat/AppHost/AppHost.cs");
        var composed = Regex.Matches(appHost, @"\.WithModule<([A-Za-z0-9_]+)")
            .Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal);
        Assert.Equal(composed, docker.Select(entry => Type.GetType(entry.Module, throwOnError: true)!.Name).Order(StringComparer.Ordinal));
        Assert.All(docker.Concat(profile), entry =>
        {
            var type = Type.GetType(entry.Module, throwOnError: true)!;
            Assert.True(typeof(DigitalBrain.Core.IModule).IsAssignableFrom(type));
            Assert.False(DigitalBrain.Contracts.PlatformAssemblyAttribute.IsPlatform(type.Assembly));
            Assert.DoesNotContain(type.Assembly.GetReferencedAssemblies(), reference => reference.Name == "DigitalBrain.Platform");

        });
    }
}
