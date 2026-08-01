using DigitalBrain.AI;
using Xunit;

namespace DigitalBrain.ModuleTests;

public sealed class BehaviorAuthoring
{
    [Fact(DisplayName = "natural-language request returns a feature/scenario diff before source changes")]
    public void ProposeScenariosReturnsFeatureDiffWithoutCode()
    {
        var author = new BehaviorAuthor();
        var proposal = author.ProposeScenarios(new BehaviorChangeRequest(
            BehaviorId: "com.demo",
            RequestText: "also enrich phone numbers",
            CurrentFeatureText: "Feature: demo\n  Scenario: base\n",
            CurrentProgramSource: "public sealed class Program {}",
            DisplayName: "Demo",
            FeatureName: "install"));

        Assert.Contains("Scenario: also enrich phone numbers", proposal.ProposedFeatureText, StringComparison.Ordinal);
        Assert.DoesNotContain("class ", proposal.ProposedFeatureText, StringComparison.Ordinal);
        Assert.True(proposal.RequiresApproval);
        Assert.Contains("before any source generation", proposal.DiffSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "approved scenarios can produce a propose-ready change without auto-publishing")]
    public void ApplyApprovedScenariosIsProposeReadyOnly()
    {
        var author = new BehaviorAuthor();
        var request = new BehaviorChangeRequest(
            "com.demo",
            "also enrich phone numbers",
            "Feature: demo\n",
            "public sealed class Program {}",
            "Demo",
            "install");
        var proposal = author.ProposeScenarios(request);
        var result = author.ApplyApprovedScenarios(request, proposal);

        Assert.True(result.ReadyForPropose);
        Assert.Equal(proposal.ProposedFeatureText, result.FeatureText);
        Assert.Equal(request.CurrentProgramSource, result.ProgramSource);
    }
}
