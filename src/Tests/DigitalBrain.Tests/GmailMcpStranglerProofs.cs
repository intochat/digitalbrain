using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Modules.Sdk.Mcp;
using Xunit;

namespace DigitalBrain.Tests;

// S1.6: Gmail is definition + capability only; typed path and Google.Apis pins are gone.
public sealed class GmailMcpStranglerProofs
{
    [Fact]
    public void GoogleModuleRegistersGmailAsMcpServerDefinitionOnly()
    {
        var source = ReadRepoFile("src", "Modules", "Google", "Google", "GoogleModule.cs");

        Assert.Contains("McpServerDefinition", source, StringComparison.Ordinal);
        Assert.Contains("ExternalServerCapability", source, StringComparison.Ordinal);
        Assert.Contains(GoogleModule.GmailServerKey, source, StringComparison.Ordinal);
        Assert.Contains("gmailmcp.googleapis.com", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GmailAuthRail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Google.Apis", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GmailServerKeyIsAValidNeuronInstanceName()
    {
        // Slash keys are rejected by IdentityPart — google.gmail (not google/gmail) is required.
        var id = new NeuronId("mcp", new OwnerId("dev"), GoogleModule.GmailServerKey);
        Assert.Equal(GoogleModule.GmailServerKey, id.Name);
        Assert.Equal($"mcp:dev/{GoogleModule.GmailServerKey}", id.ToString());
    }

    [Fact]
    public void TypedGmailContractsAndAuthPathAreDeleted()
    {
        var googleRoot = RepoPath("src", "Modules", "Google");
        Assert.False(Directory.Exists(Path.Combine(googleRoot, "Google", "Gmail")));
        Assert.False(Directory.Exists(Path.Combine(googleRoot, "Google", "Auth")));
        Assert.False(Directory.Exists(Path.Combine(googleRoot, "Contracts", "Gmail")));

        var contractsDir = Path.Combine(googleRoot, "Contracts");
        var contractsCs = Directory.Exists(contractsDir)
            ? Directory.GetFiles(contractsDir, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToArray()
            : [];
        Assert.Empty(contractsCs);

        var googleCs = Directory.GetFiles(
                Path.Combine(googleRoot, "Google"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["GoogleModule.cs"], googleCs);
    }

    [Fact]
    public void GoogleApisPackagePinsAreRemoved()
    {
        var props = ReadRepoFile("Directory.Packages.props");
        Assert.DoesNotContain("Google.Apis.Auth", props, StringComparison.Ordinal);
        Assert.DoesNotContain("Google.Apis.Gmail", props, StringComparison.Ordinal);

        var csproj = ReadRepoFile("src", "Modules", "Google", "Google", "DigitalBrain.Modules.Google.csproj");
        Assert.DoesNotContain("Google.Apis", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.Extensions.AI", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyGoogleContractsAssemblyReflectsNoGmailVocabulary()
    {
        // Implementation assembly has no IHandle / typed Gmail neurons — only IModule.
        var neuronTypes = typeof(GoogleModule).Assembly
            .GetTypes()
            .Where(type => typeof(INeuron).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
            .ToArray();
        Assert.Empty(neuronTypes);

        // Production contracts list no longer includes Google contracts (Salesforce shape).
        var composition = ReadRepoFile("src", "Kernel", "DigitalBrain.Kernel", "DigitalBrainComposition.cs");
        Assert.DoesNotContain("IGmail", composition, StringComparison.Ordinal);
        Assert.Contains("GoogleModule", composition, StringComparison.Ordinal);

        // Contracts project ships empty (no .cs sources) — same as Salesforce.
        var contractsCsproj = ReadRepoFile(
            "src", "Modules", "Google", "Contracts", "DigitalBrain.Modules.Google.Contracts.csproj");
        Assert.Contains("empty", contractsCsproj, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestReflectionFindsNoDeadGmailContracts()
    {
        // Sdk MCP contracts remain the surface; Google has no contracts assembly types.
        var mcpManifest = ModuleReflection.ManifestOf(typeof(IMcp).Assembly);
        Assert.Contains(mcpManifest.Neurons, neuron => neuron.ContractId == "mcp");
        Assert.Contains(mcpManifest.Facts, fact => fact.ContractId == "db.mcp.list-tools");
        Assert.Contains(mcpManifest.Facts, fact => fact.ContractId == "db.mcp.call-tool");

        Assert.DoesNotContain(
            mcpManifest.Facts,
            fact => fact.ContractId.Contains("gmail", StringComparison.OrdinalIgnoreCase)
                || fact.ContractId.Contains("google.gmail-", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadRepoFile(params string[] relativeParts)
    {
        var path = RepoPath(relativeParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Could not locate file {string.Join('/', relativeParts)}.");
        }

        return File.ReadAllText(path);
    }

    private static string RepoPath(params string[] relativeParts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "DigitalBrain.slnx");
            if (File.Exists(marker))
            {
                return Path.Combine([dir.FullName, .. relativeParts]);
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate repo root (DigitalBrain.slnx) from the test base directory.");
    }
}
