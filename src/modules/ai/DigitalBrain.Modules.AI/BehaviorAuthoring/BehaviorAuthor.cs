namespace DigitalBrain.AI;

public sealed class BehaviorAuthor : IBehaviorAuthor
{
    public BehaviorScenarioProposal ProposeScenarios(BehaviorChangeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RequestText);

        var title = Collapse(request.RequestText);
        var baseFeature = string.IsNullOrWhiteSpace(request.CurrentFeatureText)
            ? $"Feature: {request.DisplayName}\n"
            : request.CurrentFeatureText.TrimEnd() + "\n";
        var proposed =
            baseFeature
            + $"  Scenario: {title}\n"
            + "    Given the requested change is approved\n"
            + "    When the behavior runs\n"
            + "    Then the outcome matches the request\n";

        return new BehaviorScenarioProposal(
            Guid.NewGuid().ToString("N"),
            proposed,
            DiffSummary: $"Add scenario '{title}' before any source generation.");
    }

    public BehaviorChangeResult ApplyApprovedScenarios(
        BehaviorChangeRequest request,
        BehaviorScenarioProposal approved)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(approved);
        ArgumentException.ThrowIfNullOrWhiteSpace(approved.ProposedFeatureText);

        var program = string.IsNullOrWhiteSpace(request.CurrentProgramSource)
            ? string.Empty
            : request.CurrentProgramSource;

        return new BehaviorChangeResult(
            program,
            approved.ProposedFeatureText,
            request.FeatureName,
            ReadyForPropose: true);
    }

    private static string Collapse(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var joined = string.Join(' ', parts);
        return joined.Length <= 80 ? joined : joined[..80].TrimEnd();
    }
}
