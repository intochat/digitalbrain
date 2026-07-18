using System.Text.Json;
using DigitalBrain.Kernel.Capabilities;
using DigitalBrain.Kernel.Contracts;
namespace DigitalBrain.Integrations.Salesforce;

internal sealed class SalesforceEnrichmentFeatureTemplate : IFeatureDraftTemplate
{
    private const string RuntimeSeedProject = "src/RuntimeAuthoredFeature/RuntimeAuthoredFeature.csproj";
    private const string ImplementationProject = "features/EnrichSalesforce/DigitalBrain.Features.EnrichSalesforce.csproj";
    private const string ScenarioProject = "features/EnrichSalesforce.Tests/DigitalBrain.Features.EnrichSalesforce.Tests.csproj";
    private const string SyntheticMessageId = "synthetic-demo-priya-northstar";
    private const string SyntheticThreadId = "synthetic-thread-priya-northstar";
    private static readonly (string Path, string Resource)[] SourceFiles =
    [
        (ImplementationProject, "DigitalBrain.FeatureTemplates.EnrichSalesforce.DigitalBrain.Features.EnrichSalesforce.csproj"),
        ("features/EnrichSalesforce/EnrichSalesforce.cs", "DigitalBrain.FeatureTemplates.EnrichSalesforce.EnrichSalesforce.cs"),
        (ScenarioProject, "DigitalBrain.FeatureTemplates.EnrichSalesforce.DigitalBrain.Features.EnrichSalesforce.Tests.csproj"),
        ("features/EnrichSalesforce.Tests/EnrichSalesforce.feature", "DigitalBrain.FeatureTemplates.EnrichSalesforce.EnrichSalesforce.feature"),
        ("features/EnrichSalesforce.Tests/EnrichSalesforceSteps.cs", "DigitalBrain.FeatureTemplates.EnrichSalesforce.EnrichSalesforceSteps.cs"),
        ("features/EnrichSalesforce.Tests/reqnroll.json", "DigitalBrain.FeatureTemplates.EnrichSalesforce.reqnroll.json")
    ];
    public string OpenedText => "Enrich Salesforce is ready in Feature Studio.";

    public bool SupportsDraft(string prompt)
    {
        var normalized = prompt.ToLowerInvariant();
        return normalized.Contains("feature", StringComparison.Ordinal) &&
               normalized.Contains("enrich", StringComparison.Ordinal) &&
               normalized.Contains("salesforce", StringComparison.Ordinal) &&
               normalized.Contains("gmail", StringComparison.Ordinal);
    }

    public bool SupportsOpen(string prompt) =>
        prompt.StartsWith("open ", StringComparison.Ordinal) &&
        prompt.Contains("enrich", StringComparison.Ordinal) &&
        prompt.Contains("salesforce", StringComparison.Ordinal);

    public bool MatchesDraft(FeatureDraft draft) =>
        draft.Source.ImplementationProjectPath.Equals(ImplementationProject, StringComparison.Ordinal) ||
        draft.Goal.Contains("enrich", StringComparison.OrdinalIgnoreCase) &&
        draft.Goal.Contains("salesforce", StringComparison.OrdinalIgnoreCase) &&
        draft.Goal.Contains("gmail", StringComparison.OrdinalIgnoreCase);

    public async Task<FeatureDraft> SeedAsync(
        IFeatureHubGrain hub,
        FeatureDraft draft,
        string operationId,
        DateTimeOffset revisedAt)
    {
        if (draft.Revision == 0)
        {
            draft = await hub.ReviseBehaviorAsync(new ReviseFeatureBehavior(
                draft.DraftId,
                Behavior(),
                draft.Revision,
                operationId + "-seed-behavior",
                revisedAt)).ConfigureAwait(false);
        }
        if (string.Equals(draft.Source.ImplementationProjectPath, RuntimeSeedProject, StringComparison.Ordinal))
        {
            draft = await hub.ReviseSourceAsync(new ReviseFeatureSource(
                draft.DraftId,
                Source(),
                draft.Revision,
                operationId + "-seed-source",
                revisedAt)).ConfigureAwait(false);
        }
        return draft;
    }

    public bool TryCreatePayload(
        CapabilityDescriptor descriptor,
        string prompt,
        out RetainedInoCapabilityPayload payload)
    {
        var request = prompt.ToLowerInvariant();
        var feature = (descriptor.Name + " " + descriptor.Description).ToLowerInvariant();
        if (descriptor.Origin != CapabilityOrigin.Feature ||
            !request.Contains("synthetic", StringComparison.Ordinal) ||
            !request.Contains("demo", StringComparison.Ordinal) ||
            !request.Contains("message", StringComparison.Ordinal) ||
            !feature.Contains("enrich", StringComparison.Ordinal) ||
            !feature.Contains("salesforce", StringComparison.Ordinal) ||
            !feature.Contains("gmail", StringComparison.Ordinal))
        {
            payload = null!;
            return false;
        }
        payload = new RetainedInoCapabilityPayload(
            descriptor.Id,
            JsonSerializer.SerializeToElement(new
            {
                messageId = SyntheticMessageId,
                threadId = SyntheticThreadId,
                syntheticDemo = true,
                syntheticLabel = "Synthetic demo message — Priya Natarajan from Northstar Robotics about the pilot rollout."
            }));
        return true;
    }

    private static FeatureBehavior Behavior() => new(
    [
        new FeatureScenario(
            "enrich-single-match",
            "Enrich the single matching Salesforce account",
            "a clearly labelled synthetic or real Gmail message identifies a company and exactly one Salesforce Account matches",
            "the installed Feature reads the Gmail message, researches the company through web search, and prepares the Account enrichment",
            "exactly one Account Description diff is proposed for explicit approval"),
        new FeatureScenario(
            "enrich-no-match",
            "Refuse to update when no Salesforce account matches",
            "a Gmail message identifies a company and Salesforce returns no matching Account",
            "the Feature attempts account resolution",
            "the run fails honestly and proposes no Salesforce update"),
        new FeatureScenario(
            "enrich-ambiguous",
            "Refuse to update when the Salesforce account is ambiguous",
            "a Gmail message identifies a company and Salesforce returns more than one matching Account",
            "the Feature attempts account resolution",
            "the run fails honestly and proposes no Salesforce update")
    ]);

    private static FeatureSourceSnapshot Source() => new(
        ImplementationProject,
        ScenarioProject,
        SourceFiles.Select(static item => new FeatureSourceFile(item.Path, Read(item.Resource))).ToArray());

    private static string Read(string resource)
    {
        var assembly = typeof(SalesforceEnrichmentFeatureTemplate).Assembly;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Feature template resource '{resource}' is unavailable.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
