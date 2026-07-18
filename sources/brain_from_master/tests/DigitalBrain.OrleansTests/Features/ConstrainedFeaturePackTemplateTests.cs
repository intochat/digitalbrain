using DigitalBrain.Kernel.Features;

namespace DigitalBrain.OrleansTests.Features;

public sealed class ConstrainedFeaturePackTemplateTests
{
    [Fact]
    public void Enrich_Salesforce_goal_seeds_exact_name_behavior_and_pack_source()
    {
        const string goal = "Enrich Salesforce account from Gmail";

        Assert.True(ConstrainedFeaturePackTemplates.TryMatchEnrichSalesforce(goal));
        Assert.True(ConstrainedFeaturePackTemplates.TryMatchEnrichSalesforce("  enrich salesforce account from gmail  "));

        var behavior = ConstrainedFeaturePackTemplates.SeedBehavior(goal);
        Assert.Equal(3, behavior.Scenarios.Length);
        Assert.Contains(behavior.Scenarios, scenario => scenario.Name.Contains("Salesforce", StringComparison.Ordinal));

        var source = ConstrainedFeaturePackTemplates.SeedSource(goal);
        Assert.Equal(
            "features/EnrichSalesforce/DigitalBrain.Features.EnrichSalesforce.csproj",
            source.ImplementationProjectPath);
        Assert.Equal(
            "features/EnrichSalesforce.Tests/DigitalBrain.Features.EnrichSalesforce.Tests.csproj",
            source.ScenarioProjectPath);
        Assert.Contains(
            source.Files,
            file => file.Path.Equals("features/EnrichSalesforce/EnrichSalesforce.cs", StringComparison.Ordinal));
        Assert.Contains(
            source.Files,
            file => file.Path.Equals(
                "integrations/DigitalBrain.Integrations.Web.Contracts/WebSearchContracts.cs",
                StringComparison.Ordinal));
        Assert.Contains(
            source.Files,
            file => file.Content.Contains("IGmailMessageReader", StringComparison.Ordinal) &&
                    file.Content.Contains("IWebSearchReader", StringComparison.Ordinal) &&
                    file.Content.Contains("ISalesforceUpdateProposer", StringComparison.Ordinal));
    }

    [Fact]
    public void Unrelated_goal_keeps_placeholder_seed()
    {
        var source = ConstrainedFeaturePackTemplates.SeedSource("Write a weekly status note");
        Assert.Equal("src/RuntimeAuthoredFeature/RuntimeAuthoredFeature.csproj", source.ImplementationProjectPath);
        Assert.Equal(2, source.Files.Length);
    }
}
