using DigitalBrain.Apps;
using DigitalBrain.Broker;

namespace DigitalBrain.Marketplace;

// The trust pipeline. It runs every gate in order and records the evidence on the listing: namespace
// proof, require-mode signature, scans, sandbox scenarios, declared-versus-used diff from the broker,
// and golden-prompt precision. A risky data class does not fail certification; it queues a human.
public sealed class CertificationService(
    INamespaceProofAuthority namespaceProof,
    IAppSignatureVerifier signatureVerifier,
    IManifestScanner scanner,
    IManifestScenarioRunner scenarioRunner,
    IGoldenPromptEvaluator goldenPrompts,
    IAppObservationStore observations) : ICertificationService
{
    public const double MinimumGoldenPromptPrecision = 0.5;

    public async Task<CertificationEvidence> CertifyAsync(CertificationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var manifest = request.Manifest;
        var challenge = namespaceProof.IssueChallenge(request.Publisher.Namespace, request.Publisher.Domain);
        var namespaceEvidence = namespaceProof.Verify(challenge);
        var signature = signatureVerifier.Verify(manifest, request.Signature);
        var findings = scanner.Scan(manifest, request.Artifact);
        var scenarios = await scenarioRunner.RunAsync(manifest, cancellationToken).ConfigureAwait(false);
        var diff = DeclaredVersusUsedDiff.From(observations.Diff(manifest));
        var golden = await goldenPrompts.EvaluateAsync(manifest, cancellationToken).ConfigureAwait(false);
        var risky = manifest.Permissions
            .Select(permission => permission.SemanticTypeId)
            .Where(RiskyDataClasses.Types.Contains)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeId => typeId, StringComparer.Ordinal)
            .ToArray();

        var failures = Failures(request, namespaceEvidence, signature, findings, scenarios, diff, golden);
        var outcome = failures.Count > 0
            ? CertificationOutcome.Rejected
            : risky.Length > 0
                ? CertificationOutcome.PendingHumanReview
                : CertificationOutcome.Certified;

        return new CertificationEvidence
        {
            AppId = manifest.Id,
            Version = manifest.Version,
            Outcome = outcome,
            NamespaceProof = namespaceEvidence,
            Signature = signature,
            Findings = findings,
            Scenarios = scenarios,
            Diff = diff,
            GoldenPrompts = golden,
            RiskyDataClasses = risky,
            Failures = failures,
            CertifiedAt = DateTimeOffset.UtcNow,
        };
    }

    private static List<string> Failures(
        CertificationRequest request,
        NamespaceProofEvidence namespaceEvidence,
        SignatureOutcome signature,
        IReadOnlyList<ScanFinding> findings,
        IReadOnlyList<ScenarioResult> scenarios,
        DeclaredVersusUsedDiff diff,
        GoldenPromptReport golden)
    {
        var failures = new List<string>();
        if (request.Manifest.Kind == AppKind.Remote && request.Publisher.Ring != PublishRing.InvitedRemote)
        {
            failures.Add("Only invited remote developers may publish remote apps.");
        }
        if (!namespaceEvidence.Verified)
        {
            failures.Add($"Namespace proof failed: {namespaceEvidence.Outcome}.");
        }
        if (signature != SignatureOutcome.Valid)
        {
            failures.Add($"Signature verification failed: {signature}.");
        }
        if (findings.Any(finding => finding.Severity == ScanSeverity.Error))
        {
            failures.Add("The scan found a blocking issue.");
        }
        if (scenarios.Any(scenario => !scenario.Passed))
        {
            failures.Add("At least one scenario failed in the sandbox.");
        }
        if (!diff.IsClean)
        {
            failures.Add("Declared-versus-used diff is not clean.");
        }
        if (golden.Precision < MinimumGoldenPromptPrecision)
        {
            failures.Add("Golden-prompt precision is below the threshold.");
        }

        return failures;
    }
}
