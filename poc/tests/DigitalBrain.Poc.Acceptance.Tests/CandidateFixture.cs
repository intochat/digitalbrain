using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed record CandidateFixture(
    CandidateFamilyId Family,
    VerifiedCandidateModule Module,
    CandidateManifest Manifest,
    string LocalSynapseAlias,
    IReadOnlyList<TrustedChartFixture> TrustedCharts);
