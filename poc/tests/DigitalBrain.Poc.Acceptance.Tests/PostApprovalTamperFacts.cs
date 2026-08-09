using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;
using DigitalBrain.Poc.Social.Contracts;
using Xunit;

namespace DigitalBrain.Poc.Acceptance.Tests;

public sealed class PostApprovalTamperFacts
{
    [Fact]
    public Task SourceTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.Source);

    [Fact]
    public Task AssemblyTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.Assembly);

    [Fact]
    public Task CandidateMetadataTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.CandidateMetadata);

    [Fact]
    public Task FixedHeaderTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.FixedHeader);

    [Fact]
    public Task FixedReferenceTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.FixedReference);

    [Fact]
    public Task CapabilityGrantTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.CapabilityGrant);

    [Fact]
    public Task QuarantineEvidenceTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.QuarantineEvidence);

    [Fact]
    public Task SignedAttestationTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.SignedAttestation);

    [Fact]
    public Task SignedApprovalTamperLeavesThePriorActivePointerAndProcessUnchanged() =>
        AssertTamperRejectedAsync(PostApprovalTamper.SignedApproval);

    private static async Task AssertTamperRejectedAsync(PostApprovalTamper tamper)
    {
        await using var root = PocDataRoot.Create(HostProcess.FindPocRoot());
        var repository = new CandidateRepository();
        var owners = new TestOwnerAuthority();
        var attestations = owners.CreateAttestationSigner();
        var approvals = owners.CreateOwnerApprovalSigner();
        var pointers = owners.CreatePointerSigner();
        var store = new TrustedCandidateCatalogStore(root, attestations, approvals, pointers);
        var catalog = new CandidateCatalog(store);
        var activeCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.DefaultTrustedFixture,
            root,
            TestContext.Current.CancellationToken);
        var proposedCandidate = await new FileCandidateCompiler(repository).CompileAsync(
            ElonChartAuthoringIntent.ForTrustedFixture(
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                "post-approval-tamper",
                "elonmusk"),
            root,
            TestContext.Current.CancellationToken);
        await PromotionFacts.AttestAndApproveAsync(
            activeCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await PromotionFacts.AttestAndApproveAsync(
            proposedCandidate,
            root,
            store,
            attestations,
            catalog,
            owners);
        await using var supervisor = new HostSupervisor(root, store, pointers, owners);
        var initial = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            activeCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(initial.Succeeded);
        var priorHead = await store.ReadPointerHeadAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);

        await TamperAsync(tamper, root, proposedCandidate, TestContext.Current.CancellationToken);
        var rejected = await supervisor.PromoteAsync(
            owners.PrincipalForTest("owner-a"),
            proposedCandidate.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(rejected.Succeeded);
        Assert.Equal(
            priorHead,
            await store.ReadPointerHeadAsync(
                owners.PrincipalForTest("owner-a"),
                ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
                TestContext.Current.CancellationToken));
        var active = await supervisor.CurrentAsync(
            owners.PrincipalForTest("owner-a"),
            ElonChartAuthoringIntent.DefaultTrustedFixture.Family,
            TestContext.Current.CancellationToken);
        Assert.Same(initial.Attachment, active);
        Assert.Equal(initial.ProcessId, active.ProcessId);
        await active.FireTrustedAsync(
            owners.SessionFor("owner-a"),
            new SocialPostObserved(
                $"still-active-{tamper}",
                "elonmusk",
                DateTimeOffset.UnixEpoch),
            TestContext.Current.CancellationToken);
    }

    internal static async Task TamperAsync(
        PostApprovalTamper tamper,
        PocDataRoot root,
        FileCandidateCompiler.CompiledCandidate candidate,
        CancellationToken cancellationToken)
    {
        switch (tamper)
        {
            case PostApprovalTamper.Source:
                await AppendAsync(candidate.SourcePath, "\n// tampered source\n", cancellationToken);
                return;
            case PostApprovalTamper.Assembly:
                await AppendAsync(candidate.AssemblyPath, "tampered assembly", cancellationToken);
                return;
            case PostApprovalTamper.CandidateMetadata:
                await AppendAsync(
                    Path.Combine(candidate.Directory, "candidate.json"),
                    "\n{\"tampered\":true}\n",
                    cancellationToken);
                return;
            case PostApprovalTamper.FixedHeader:
                await ReplaceAsync(
                    candidate.SourcePath,
                    "#:property PublishAot=false",
                    "#:property PublishAot=true",
                    cancellationToken);
                return;
            case PostApprovalTamper.FixedReference:
                await ReplaceAsync(
                    candidate.SourcePath,
                    "#:project ../../../src/DigitalBrain.Poc.Abstractions/DigitalBrain.Poc.Abstractions.csproj",
                    "#:project ../../tampered-reference.csproj",
                    cancellationToken);
                return;
            case PostApprovalTamper.CapabilityGrant:
                await MutateSignedRecordAsync(
                    Path.Combine(root.ControlPlaneRoot, "attestations", candidate.Id + ".json"),
                    node => node["payload"]!["grantedTargetScopes"] = new JsonArray("tampered-scope"),
                    cancellationToken);
                return;
            case PostApprovalTamper.QuarantineEvidence:
                await MutateSignedRecordAsync(
                    Path.Combine(root.ControlPlaneRoot, "attestations", candidate.Id + ".json"),
                    node => node["payload"]!["scenarioHash"] = new string('0', 64),
                    cancellationToken);
                return;
            case PostApprovalTamper.SignedAttestation:
                await MutateSignedRecordAsync(
                    Path.Combine(root.ControlPlaneRoot, "attestations", candidate.Id + ".json"),
                    node => node["signature"] = "corrupt",
                    cancellationToken);
                return;
            case PostApprovalTamper.SignedApproval:
                await MutateSignedRecordAsync(
                    Path.Combine(root.ControlPlaneRoot, "approvals", candidate.Id + ".json"),
                    node => node["signature"] = "corrupt",
                    cancellationToken);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(tamper), tamper, null);
        }
    }

    private static async Task AppendAsync(
        string path,
        string value,
        CancellationToken cancellationToken)
    {
        ClearReadOnly(path);
        await File.AppendAllTextAsync(path, value, cancellationToken);
    }

    private static async Task ReplaceAsync(
        string path,
        string oldValue,
        string newValue,
        CancellationToken cancellationToken)
    {
        ClearReadOnly(path);
        var contents = await File.ReadAllTextAsync(path, cancellationToken);
        Assert.Contains(oldValue, contents, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            path,
            contents.Replace(oldValue, newValue, StringComparison.Ordinal),
            cancellationToken);
    }

    private static async Task MutateSignedRecordAsync(
        string path,
        Action<JsonObject> mutate,
        CancellationToken cancellationToken)
    {
        ClearReadOnly(path);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject() ??
            throw new InvalidDataException("The signed record is not a JSON object.");
        mutate(node);
        await File.WriteAllTextAsync(
            path,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);
    }

    private static void ClearReadOnly(string path) =>
        File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);

    public enum PostApprovalTamper
    {
        Source,
        Assembly,
        CandidateMetadata,
        FixedHeader,
        FixedReference,
        CapabilityGrant,
        QuarantineEvidence,
        SignedAttestation,
        SignedApproval,
    }
}
