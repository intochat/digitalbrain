namespace DigitalBrain.UI;

internal static class BrainCapabilityCatalog
{
    internal const string GeneralAssistant = "assistant.general";
    internal const string AccountEnrichment =
        "account-enrichment.gmail-salesforce-description";

    private const string AIModule = "DigitalBrain.AI.AIModule";
    private const string ChatModule = "DigitalBrain.Chat.ChatModule";
    private const string GoogleModule = "DigitalBrain.Google.GoogleModule";
    private const string OSModule = "DigitalBrain.OS.OSBehaviorsModule";
    private const string SalesforceModule = "DigitalBrain.Salesforce.SalesforceModule";
    private const string Gemma4Feature = "ai.llm.gemma4";
    private const string GmailFeature = "google.gmail";
    private const string SalesforceFeature = "salesforce";

    internal static IReadOnlyList<BrainCapability> Resolve(
        IReadOnlyList<BrainModule> modules,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleIds = modules
            .Select(static module => module.Id)
            .ToHashSet(StringComparer.Ordinal);
        var features = configuration
            .GetSection("DigitalBrain:ConfiguredFeatures")
            .GetChildren()
            .Select(static section => section.Value)
            .Where(static feature => !string.IsNullOrWhiteSpace(feature))
            .ToHashSet(StringComparer.Ordinal);
        var generalAssistant =
            moduleIds.Contains(AIModule) &&
            moduleIds.Contains(ChatModule) &&
            moduleIds.Contains(OSModule) &&
            features.Contains(Gemma4Feature);
        var accountEnrichment =
            generalAssistant &&
            moduleIds.Contains(GoogleModule) &&
            moduleIds.Contains(SalesforceModule) &&
            features.Contains(GmailFeature) &&
            features.Contains(SalesforceFeature);

        var capabilities = new List<BrainCapability>(2);
        if (generalAssistant)
        {
            capabilities.Add(new BrainCapability(GeneralAssistant));
        }

        if (accountEnrichment)
        {
            capabilities.Add(new BrainCapability(AccountEnrichment));
        }

        return capabilities;
    }
}
