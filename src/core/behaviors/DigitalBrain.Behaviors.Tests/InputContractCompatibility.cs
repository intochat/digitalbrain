using DigitalBrain.Behaviors.Manifest;
using Xunit;
using DigitalBrain.Behaviors.Runtime;

namespace DigitalBrain.Behaviors.Tests;

public sealed class InputContractCompatibility
{
    private const string AlphaPayload = "{\"type\":\"object\",\"properties\":{\"A\":{\"type\":\"string\"}}}";
    private const string ZuluPayload = "{\"type\":\"object\",\"properties\":{\"Z\":{\"type\":\"string\"}}}";
    private const string BetaPayload = "{\"type\":\"object\",\"properties\":{\"B\":{\"type\":\"string\"}}}";
    private const string AlphaIntPayload = "{\"type\":\"object\",\"properties\":{\"A\":{\"type\":\"integer\"}}}";

    [Fact(DisplayName = "Reordered union cases remain compatible without a major version")]
    public void ReorderIsCompatible()
    {
        var prior = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
            Case("case.zulu", "Zulu", ZuluPayload),
        ]);
        var next = Contract(
        [
            Case("case.zulu", "Zulu", ZuluPayload),
            Case("case.alpha", "Alpha", AlphaPayload),
        ]);

        var result = BehaviorContractCompatibility.Assess(prior, next);

        Assert.True(result.IsCompatible, result.Detail);
        Assert.False(result.RequiresMajorVersion);
        Assert.False(result.RequiresCaseIdMapping);
    }

    [Fact(DisplayName = "Renamed case requires explicit case-ID mapping")]
    public void RenameRequiresCaseIdMapping()
    {
        var prior = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
            Case("case.zulu", "Zulu", ZuluPayload),
        ]);
        var next = Contract(
        [
            Case("case.alpha-renamed", "AlphaRenamed", AlphaPayload),
            Case("case.zulu", "Zulu", ZuluPayload),
        ]);

        var unmapped = BehaviorContractCompatibility.Assess(prior, next);
        Assert.False(unmapped.IsCompatible);
        Assert.True(unmapped.RequiresCaseIdMapping, unmapped.Detail);

        var mapped = BehaviorContractCompatibility.Assess(
            prior,
            next,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["case.alpha"] = "case.alpha-renamed",
            });
        Assert.True(mapped.IsCompatible, mapped.Detail);
        Assert.False(mapped.RequiresMajorVersion);
    }

    [Fact(DisplayName = "Adding or removing a case requires a contract major version")]
    public void AddOrRemoveRequiresMajorVersion()
    {
        var prior = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
            Case("case.zulu", "Zulu", ZuluPayload),
        ]);
        var added = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
            Case("case.zulu", "Zulu", ZuluPayload),
            Case("case.beta", "Beta", BetaPayload),
        ]);
        var removed = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
        ]);

        var addResult = BehaviorContractCompatibility.Assess(prior, added);
        Assert.False(addResult.IsCompatible);
        Assert.True(addResult.RequiresMajorVersion, addResult.Detail);

        var removeResult = BehaviorContractCompatibility.Assess(prior, removed);
        Assert.False(removeResult.IsCompatible);
        Assert.True(removeResult.RequiresMajorVersion, removeResult.Detail);
    }

    [Fact(DisplayName = "Replacing a case payload requires a contract major version")]
    public void ReplacePayloadRequiresMajorVersion()
    {
        var prior = Contract(
        [
            Case("case.alpha", "Alpha", AlphaPayload),
        ]);
        var next = Contract(
        [
            Case("case.alpha", "Alpha", AlphaIntPayload),
        ]);

        var result = BehaviorContractCompatibility.Assess(prior, next);

        Assert.False(result.IsCompatible);
        Assert.True(result.RequiresMajorVersion, result.Detail);
    }

    private static BehaviorContractManifest Contract(IReadOnlyList<BehaviorContractCaseManifest> cases)
        => new(
            "com.digitalbrain.sample",
            1,
            "{\"oneOf\":[]}",
            cases,
            "{\"type\":\"object\"}");

    private static BehaviorContractCaseManifest Case(string id, string name, string payload)
        => new(id, 1, name, payload);
}
