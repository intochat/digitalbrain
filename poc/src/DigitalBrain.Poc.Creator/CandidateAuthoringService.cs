using System;
using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Poc.ControlPlane;
using DigitalBrain.Poc.Runtime;

namespace DigitalBrain.Poc.Creator;

public sealed class CandidateAuthoringService
{
    private readonly PocDataRoot _root;
    private readonly CandidateRepository _repository;
    private readonly CandidateFamilyMinter _families;

    public CandidateAuthoringService(CandidateRepository repository, PocDataRoot root)
        : this(
            repository,
            root,
            new CandidateFamilyMinter(new FileCandidateFamilyRegistry(root)))
    {
    }

    internal CandidateAuthoringService(
        CandidateRepository repository,
        PocDataRoot root,
        CandidateFamilyMinter families)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _families = families ?? throw new ArgumentNullException(nameof(families));
    }

    public async Task<OwnerBoundCompiledCandidate> CompileAsync(
        AuthenticatedPrincipal owner,
        string chartId,
        string expectedAuthor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var family = await _families.MintAndReserveAsync(owner, cancellationToken);
        var intent = ElonChartAuthoringIntent.ForReservedFamily(
            family,
            chartId,
            expectedAuthor);
        var compiled = await new FileCandidateCompiler(_repository).CompileAsync(
            intent,
            _root,
            cancellationToken);
        return new OwnerBoundCompiledCandidate(compiled, owner);
    }
}
