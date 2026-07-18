using System.Text;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Features;

internal static class ConstrainedFeaturePackTemplates
{
    internal const string EnrichSalesforceAccountFromGmail = "Enrich Salesforce account from Gmail";

    private static readonly string[] EnrichSalesforceRelativePaths =
    [
        "Directory.Build.props",
        "Directory.Packages.props",
        "README.md",
        "src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj",
        "src/DigitalBrain.Features.Sdk/FeatureContracts.cs",
        "src/DigitalBrain.Features.Sdk/FeatureContext.cs",
        "src/DigitalBrain.Features.Sdk/MemoryContracts.cs",
        "integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj",
        "integrations/DigitalBrain.Integrations.Google.Contracts/GoogleCapabilities.cs",
        "integrations/DigitalBrain.Integrations.Google.Contracts/GmailContracts.cs",
        "integrations/DigitalBrain.Integrations.Salesforce.Contracts/DigitalBrain.Integrations.Salesforce.Contracts.csproj",
        "integrations/DigitalBrain.Integrations.Salesforce.Contracts/SalesforceCapabilities.cs",
        "integrations/DigitalBrain.Integrations.Salesforce.Contracts/SalesforceContracts.cs",
        "integrations/DigitalBrain.Integrations.Web.Contracts/DigitalBrain.Integrations.Web.Contracts.csproj",
        "integrations/DigitalBrain.Integrations.Web.Contracts/WebSearchCapabilities.cs",
        "integrations/DigitalBrain.Integrations.Web.Contracts/WebSearchContracts.cs",
        "src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj",
        "src/DigitalBrain.Features.Testing/FeatureDuplicateScenario.cs",
        "src/DigitalBrain.Features.Testing/FeatureScenarioContext.cs",
        "src/DigitalBrain.Features.Testing/FeatureScenarioSteps.cs",
        "src/DigitalBrain.Features.Testing/GeneratedDuplicateInput.feature",
        "src/DigitalBrain.Features.Testing/buildTransitive/DigitalBrain.Features.Testing.targets",
        "features/EnrichSalesforce/DigitalBrain.Features.EnrichSalesforce.csproj",
        "features/EnrichSalesforce/EnrichSalesforce.cs",
        "features/EnrichSalesforce.Tests/DigitalBrain.Features.EnrichSalesforce.Tests.csproj",
        "features/EnrichSalesforce.Tests/EnrichSalesforce.feature",
        "features/EnrichSalesforce.Tests/EnrichSalesforceSteps.cs",
        "features/EnrichSalesforce.Tests/reqnroll.json"
    ];

    internal static bool TryMatchEnrichSalesforce(string goal) =>
        string.Equals(
            NormalizeGoal(goal),
            NormalizeGoal(EnrichSalesforceAccountFromGmail),
            StringComparison.OrdinalIgnoreCase);

    internal static FeatureBehavior SeedBehavior(string goal)
    {
        if (!TryMatchEnrichSalesforce(goal))
            return DefaultBehavior();

        return new FeatureBehavior(
        [
            new FeatureScenario(
                "scenario-1",
                "Enrich the single matching Salesforce account",
                "one Gmail message identifies Northstar Robotics and web evidence is available",
                "the Feature is handled",
                "exactly one Salesforce Description update is proposed"),
            new FeatureScenario(
                "scenario-2",
                "Refuse to update when no Salesforce account matches",
                "no Salesforce account matches the Gmail sender company",
                "the Feature is handled",
                "execution fails and no Salesforce update is proposed"),
            new FeatureScenario(
                "scenario-3",
                "Refuse to update when the Salesforce account is ambiguous",
                "two Salesforce accounts match the company",
                "the Feature is handled",
                "execution fails and no Salesforce update is proposed")
        ]);
    }

    internal static FeatureSourceSnapshot SeedSource(string goal)
    {
        if (!TryMatchEnrichSalesforce(goal))
            return DefaultSource();

        if (!TryResolveRepositoryRoot(out var root))
            return DefaultSource();

        var files = new List<FeatureSourceFile>(EnrichSalesforceRelativePaths.Length);
        foreach (var relative in EnrichSalesforceRelativePaths)
        {
            var full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
                return DefaultSource();
            var content = File.ReadAllText(full);
            if (Encoding.UTF8.GetByteCount(content) > FeatureLimits.DraftSourceFileUtf8Bytes)
                return DefaultSource();
            files.Add(new FeatureSourceFile(relative, content));
        }

        return new FeatureSourceSnapshot(
            "features/EnrichSalesforce/DigitalBrain.Features.EnrichSalesforce.csproj",
            "features/EnrichSalesforce.Tests/DigitalBrain.Features.EnrichSalesforce.Tests.csproj",
            files.ToArray());
    }

    private static FeatureBehavior DefaultBehavior() => new(
    [
        new FeatureScenario(
            "scenario-1",
            "Describe the intended outcome",
            "the Feature Draft is editable",
            "the Behavior is revised",
            "the intended outcome is recorded")
    ]);

    private static FeatureSourceSnapshot DefaultSource()
    {
        const string implementationProject = "src/RuntimeAuthoredFeature/RuntimeAuthoredFeature.csproj";
        const string scenarioProject = "tests/RuntimeAuthoredFeature.Scenarios/RuntimeAuthoredFeature.Scenarios.csproj";
        return new FeatureSourceSnapshot(
            implementationProject,
            scenarioProject,
            [
                new FeatureSourceFile(implementationProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>"),
                new FeatureSourceFile(scenarioProject, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>")
            ]);
    }

    private static string NormalizeGoal(string goal) =>
        string.Join(
            ' ',
            goal.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool TryResolveRepositoryRoot(out string root)
    {
        var configured = Environment.GetEnvironmentVariable("DIGITALBRAIN_REPO_ROOT");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(Path.Combine(configured, "Brain.slnx")))
        {
            root = Path.GetFullPath(configured);
            return true;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Brain.slnx")))
            {
                root = directory.FullName;
                return true;
            }

            directory = directory.Parent;
        }

        root = string.Empty;
        return false;
    }
}
