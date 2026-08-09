using System;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

public sealed record ElonChartAuthoringIntent
{
    private ElonChartAuthoringIntent(
        CandidateFamilyId family,
        string chartId,
        string expectedAuthor)
        : this(
            family,
            CandidateSemanticPolicy.SocialPostObservedAlias,
            chartId,
            expectedAuthor,
            localSynapseSchemaVersion: 1)
    {
    }

    private ElonChartAuthoringIntent(
        CandidateFamilyId family,
        string attestedTriggerAlias,
        string chartId,
        string expectedAuthor,
        int localSynapseSchemaVersion)
    {
        if (string.IsNullOrWhiteSpace(attestedTriggerAlias))
        {
            throw new ArgumentException("The attested trigger alias cannot be empty.", nameof(attestedTriggerAlias));
        }

        if (string.IsNullOrWhiteSpace(chartId))
        {
            throw new ArgumentException("The granted chart ID cannot be empty.", nameof(chartId));
        }

        if (string.IsNullOrWhiteSpace(expectedAuthor))
        {
            throw new ArgumentException("The expected author cannot be empty.", nameof(expectedAuthor));
        }

        if (localSynapseSchemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localSynapseSchemaVersion),
                "The local synapse schema version must be positive.");
        }

        Family = CandidateFamilyId.Parse(family.Value);
        AttestedTriggerAlias = attestedTriggerAlias;
        ChartId = chartId;
        ExpectedAuthor = expectedAuthor;
        LocalSynapseSchemaVersion = localSynapseSchemaVersion;
    }

    public static ElonChartAuthoringIntent DefaultTrustedFixture { get; } = new(
        CandidateFamilyId.Parse("cf_aaaaaaaaaaaaaaaaaaaaaaaaaa"),
        "elon-chart",
        "elonmusk");

    public static ElonChartAuthoringIntent ForTrustedFixture(
        CandidateFamilyId family,
        string chartId,
        string expectedAuthor) =>
        new(family, chartId, expectedAuthor);

    public static ElonChartAuthoringIntent ForTrustedFixture(
        CandidateFamilyId family,
        string attestedTriggerAlias,
        string chartId,
        string expectedAuthor,
        int localSynapseSchemaVersion) =>
        new(family, attestedTriggerAlias, chartId, expectedAuthor, localSynapseSchemaVersion);

    internal static ElonChartAuthoringIntent ForReservedFamily(
        CandidateFamilyId family,
        string chartId,
        string expectedAuthor) =>
        new(family, chartId, expectedAuthor);

    public CandidateFamilyId Family { get; }

    public string ChartId { get; }

    public string ExpectedAuthor { get; }

    public string AttestedTriggerAlias { get; }

    public int LocalSynapseSchemaVersion { get; }
}
