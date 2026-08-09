using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Creator;
using DigitalBrain.Poc.Host;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Acceptance.Tests;

internal sealed class ElonChartPocFixture : IAsyncDisposable
{
    private readonly CandidateRepository _repository = new();
    private readonly AttestationSigner _attestations;
    private readonly CandidateCatalog _catalog;

    private ElonChartPocFixture(PocDataRoot root)
    {
        Root = root;
        Owners = new TestOwnerAuthority();
        _attestations = Owners.CreateAttestationSigner();
        var approvals = Owners.CreateOwnerApprovalSigner();
        var pointers = Owners.CreatePointerSigner();
        Store = new TrustedCandidateCatalogStore(root, _attestations, approvals, pointers);
        _catalog = new CandidateCatalog(Store);
        Supervisor = new HostSupervisor(root, Store, pointers, Owners);
    }

    public PocDataRoot Root { get; }

    public TestOwnerAuthority Owners { get; }

    public TrustedCandidateCatalogStore Store { get; }

    public HostSupervisor Supervisor { get; }

    public static ElonChartPocFixture Create(PocDataRoot root) => new(root);

    public async Task<FileCandidateCompiler.CompiledCandidate> CreateApprovedAsync(
        AuthenticatedPrincipal owner,
        string chartId,
        string expectedAuthor,
        CancellationToken cancellationToken = default)
    {
        var authored = await new CandidateAuthoringService(_repository, Root).CompileAsync(
            owner,
            chartId,
            expectedAuthor,
            cancellationToken);
        await new QuarantineRunner(
            _repository,
            Store,
            _attestations,
            Owners.ExportSessions()).RunAsync(
                authored,
                Root,
                owner,
                cancellationToken);
        await _catalog.ApproveAsync(owner, authored.Id, cancellationToken);
        return authored.Candidate;
    }

    public async Task<FileCandidateCompiler.CompiledCandidate> CreateApprovedTrustedFixtureAsync(
        AuthenticatedPrincipal owner,
        ElonChartAuthoringIntent intent,
        CancellationToken cancellationToken = default)
    {
        var compiled = await new FileCandidateCompiler(_repository).CompileAsync(
            intent,
            Root,
            cancellationToken);
        await new QuarantineRunner(
            _repository,
            Store,
            _attestations,
            Owners.ExportSessions()).RunTrustedFixtureAsync(
                compiled,
                Root,
                owner,
                cancellationToken);
        await _catalog.ApproveAsync(owner, compiled.Id, cancellationToken);
        return compiled;
    }

    public async Task<HostAttachment> PromoteAsync(
        AuthenticatedPrincipal owner,
        string candidateId,
        HostFault fault = HostFault.None,
        CancellationToken cancellationToken = default)
    {
        var result = await Supervisor.PromoteAsync(owner, candidateId, fault, cancellationToken);
        return result.Succeeded
            ? result.Attachment!
            : throw new InvalidOperationException($"Promotion failed: {result.Failure}.");
    }

    public async Task<HostAttachment> RestartAsync(
        AuthenticatedPrincipal owner,
        CandidateFamilyId family,
        CancellationToken cancellationToken = default)
    {
        var result = await Supervisor.TryRestartActiveAsync(owner, family, cancellationToken);
        return result.Succeeded
            ? result.Attachment!
            : throw new InvalidOperationException($"Restart failed: {result.Failure}.");
    }

    public ValueTask DisposeAsync() => Supervisor.DisposeAsync();
}
