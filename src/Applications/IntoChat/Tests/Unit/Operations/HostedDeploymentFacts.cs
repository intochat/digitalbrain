namespace IntoChat.Tests.Operations;

public sealed class HostedDeploymentFacts
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string[] ProductModules =
    [
        "DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI",
        "DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory",
        "DigitalBrain.ClickHouse.ClickHouseModule, DigitalBrain.Modules.ClickHouse",
        "DigitalBrain.Supabase.SupabaseModule, DigitalBrain.Modules.Supabase",
        "DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time",
        "DigitalBrain.Google.Gmail.GmailModule, DigitalBrain.Modules.Google.Gmail",
        "DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce",
        "DigitalBrain.Microsoft.GitHub.GitHubModule, DigitalBrain.Modules.Microsoft.GitHub",
        "DigitalBrain.Flutter.FlutterModule, DigitalBrain.Modules.Flutter",
    ];

    private static readonly string[] DeveloperModules =
    [
        "DigitalBrain.Microsoft.Aspire.AspireModule",
        "DigitalBrain.Microsoft.Roslyn.RoslynModule",
        "DigitalBrain.Microsoft.DotNet.DotNetModule",
        "DigitalBrain.Coding.CodingModule",
        "DigitalBrain.Behavior.BehaviorModule",
    ];

    private static readonly string[] PackagingFiles =
    [
        "src/Applications/IntoChat/IntoChat/Properties/PublishProfiles/Container.pubxml",
        "src/Applications/IntoChat/IntoChat/Dockerfile",
    ];

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

    private static string Read(string relative) => File.ReadAllText(Path.Combine(RepositoryRoot, relative.Replace('/', Path.DirectorySeparatorChar)));

    [Fact]
    public void HostedPackagingBakesTheProductModulesOnly()
    {
        foreach (var file in PackagingFiles)
        {
            var text = Read(file);
            foreach (var module in ProductModules) { Assert.Contains(module, text); }
            foreach (var module in DeveloperModules) { Assert.DoesNotContain(module, text); }
            Assert.Contains("IntoChat__Hosted__Enabled", text);
        }
    }

    [Fact]
    public void HostedEntrypointRunsTheSiloOnly()
    {
        var entrypoint = Read("src/Applications/IntoChat/IntoChat/docker-entrypoint.sh");
        Assert.Contains("IntoChat.dll", entrypoint);
        Assert.DoesNotContain("mcp", entrypoint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HostedProfileForwardsManagedIdentityAndKeyVaultWithoutCallingAzure()
    {
        var profile = Read("src/Applications/IntoChat/AppHost/Profiles/HostedProfile.cs");
        Assert.Contains("AZURE_CLIENT_ID", profile);
        Assert.Contains("DigitalBrain__KeyVault__Uri", profile);
        Assert.DoesNotContain("AddAzureKeyVault", profile);
        Assert.DoesNotContain("SecretClient", profile);
    }

    [Fact]
    public void TelemetryCollectorTailSamplesToAPersistentBackend()
    {
        var collector = Read("ops/otel/collector.yaml");
        Assert.Contains("tail_sampling", collector);
        Assert.Contains("otlphttp/backend", collector);
        Assert.Contains("OTEL_BACKEND_ENDPOINT", collector);
        Assert.Contains("intochat.intent.id", collector);
    }

    [Fact]
    public void RunbookCoversIncidentBackupUpgradeAndSlo()
    {
        var runbook = Read("docs/operations/runbook.md");
        foreach (var topic in new[] { "Incident response", "Backup and restore", "Upgrade and rollback", "99.5 %" })
        {
            Assert.Contains(topic, runbook);
        }
    }
}
