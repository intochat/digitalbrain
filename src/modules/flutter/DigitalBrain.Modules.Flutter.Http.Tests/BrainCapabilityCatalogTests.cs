using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.UI.Tests;

public sealed class BrainCapabilityCatalogTests
{
    private static readonly BrainModule[] ProductModules =
    [
        new("DigitalBrain.AI.AIModule"),
        new("DigitalBrain.Chat.ChatModule"),
        new("DigitalBrain.Flutter.FlutterModule"),
        new("DigitalBrain.Google.GoogleModule"),
        new("DigitalBrain.OS.OSBehaviorsModule"),
        new("DigitalBrain.Salesforce.SalesforceModule"),
    ];

    [Fact(DisplayName =
        "all modules without configured providers claims no product capability")]
    public void CompleteModuleSetWithoutConfiguredFeaturesClaimsNothing()
    {
        var capabilities = BrainCapabilityCatalog.Resolve(
            ProductModules,
            Configuration());

        Assert.Empty(capabilities);
    }

    [Fact(DisplayName =
        "configured providers without OS responder claims no product capability")]
    public void ConfiguredFeaturesWithoutOSResponderClaimsNothing()
    {
        var modules = ProductModules
            .Where(module => module.Id != "DigitalBrain.OS.OSBehaviorsModule")
            .ToArray();

        var capabilities = BrainCapabilityCatalog.Resolve(
            modules,
            Configuration("ai.llm.gemma4", "google.gmail", "salesforce"));

        Assert.Empty(capabilities);
    }

    [Fact(DisplayName =
        "configured non-Gemma model claims no product capability")]
    public void NonGemmaModelClaimsNothing()
    {
        var capabilities = BrainCapabilityCatalog.Resolve(
            ProductModules,
            Configuration("ai.llm.llama32", "google.gmail", "salesforce"));

        Assert.Empty(capabilities);
    }

    [Fact(DisplayName =
        "configured Gemma product manifest claims general assistant and account enrichment")]
    public void ConfiguredGemmaProductManifestClaimsExactCapabilities()
    {
        var capabilities = BrainCapabilityCatalog.Resolve(
            ProductModules,
            Configuration("ai.llm.gemma4", "google.gmail", "salesforce"));

        Assert.Equal(
            [
                BrainCapabilityCatalog.GeneralAssistant,
                BrainCapabilityCatalog.AccountEnrichment,
            ],
            capabilities.Select(capability => capability.Id));
    }

    private static IConfiguration Configuration(params string[] features)
    {
        var values = features
            .Select((feature, index) =>
                new KeyValuePair<string, string?>(
                    $"DigitalBrain:ConfiguredFeatures:{index}",
                    feature));
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
