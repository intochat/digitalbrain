using DigitalBrain.Apps.Signals;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GrainType("apps.package")]
internal sealed class PackageNeuron(
    [PersistentState("package", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<PackageState> store,
    ICodeArtifactStore artifacts,
    TimeProvider clock)
    : Neuron<PackageState>(store), IPackage
{
    private PackageId Id => PackageId.Parse(this.GetPrimaryKeyString());

    public Task<PackageSnapshot> Read() => Task.FromResult(Describe(Id));

    public Task<PackageRevision> ReadRevision(string revision)
    {
        var id = Id;
        return Snapshot.Revisions.TryGetValue(revision ?? "", out var found)
            ? Task.FromResult(found)
            : throw new KeyNotFoundException($"{id} has no revision {revision}.");
    }

    public async Task<PackageRevision> Commit(CommitPackage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        var author = RequireOwner(id);
        PackageRules.Validate(request.Content);
        var message = PackageRules.Message(request.Message);
        if (Replay(request.OperationId, request) is { } committed) { return Snapshot.Revisions[committed]; }
        if (request.ExpectedHead != Snapshot.Head)
        {
            throw new InvalidOperationException(
                $"{id} is at {Snapshot.Head ?? "no revision"}, not {request.ExpectedHead ?? "no revision"}. Read the package and commit on top of its head.");
        }
        List<string> parents = Snapshot.Head is null ? [] : [Snapshot.Head];
        var arrived = new Dictionary<string, PackageRevision>();
        if (request.MergeFrom is { } merge)
        {
            if (Snapshot.Head is null) { throw new InvalidOperationException("Commit a first revision before merging."); }
            arrived = await Fetch(merge);
            if (IsAncestor(merge.Revision, Snapshot.Head, arrived))
            { throw new InvalidOperationException($"{id} already contains {merge.Package}@{merge.Revision}; commit without merging."); }
            parents.Add(merge.Revision);
        }
        await VerifyArtifact(request.Content, request.Artifact);
        var revision = new PackageRevision(PackageHash.Revision(parents, request.Content), parents, request.Content, request.Artifact, author, message, clock.GetUtcNow());
        arrived[revision.Id] = revision;
        await Persist(id, Advance(revision.Id, arrived, request.OperationId, request, revision.Id));
        return revision;
    }

    public async Task<PackageSnapshot> Fork(ForkPackage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        RequireOwner(id);
        if (Replay(request.OperationId, request) is not null) { return Describe(id); }
        if (Snapshot.Head is not null || Snapshot.ForkedFrom is not null) { throw new InvalidOperationException($"{id} already exists; fork into a new name."); }
        if (request.Source.Package == id) { throw new ArgumentException("A package cannot fork itself."); }
        var arrived = await Fetch(request.Source);
        await Persist(id, Advance(request.Source.Revision, arrived, request.OperationId, request, request.Source.Revision) with { ForkedFrom = request.Source });
        return Describe(id);
    }

    public async Task<PackageSnapshot> Pull(PullPackage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        RequireOwner(id);
        if (Replay(request.OperationId, request) is not null) { return Describe(id); }
        var head = Snapshot.Head ?? throw new InvalidOperationException($"{id} has no revision to pull into; fork instead.");
        var arrived = await Fetch(request.Source);
        if (IsAncestor(request.Source.Revision, head, arrived)) { return Describe(id); }
        if (!IsAncestor(head, request.Source.Revision, arrived))
        {
            throw new InvalidOperationException(
                $"{id} has changes that {request.Source.Package}@{request.Source.Revision} does not. Commit a merge from that revision instead of pulling.");
        }
        await Persist(id, Advance(request.Source.Revision, arrived, request.OperationId, request, request.Source.Revision));
        return Describe(id);
    }

    public async Task<PackageProposal> Propose(ProposeChange request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        var author = RequireCaller();
        if (request.Source.Package == id) { throw new ArgumentException("Propose changes from a fork of this package."); }
        if (author != request.Source.Package.Owner) { throw new UnauthorizedAccessException($"Only {request.Source.Package.Owner} can propose changes from {request.Source.Package}."); }
        var title = PackageRules.Message(request.Title);
        if (Replay(request.OperationId, request) is { } number) { return Snapshot.Proposals.Single(item => item.Number == int.Parse(number, System.Globalization.CultureInfo.InvariantCulture)); }
        await GrainFactory.GetGrain<IPackage>(request.Source.Package.ToString()).ReadRevision(request.Source.Revision);
        // A fork has at most one open proposal; proposing again moves it to the new revision, like pushing to a pull request.
        var proposals = new List<PackageProposal>(Snapshot.Proposals);
        var index = proposals.FindIndex(item => item.Status == ProposalStatus.Open && item.Source.Package == request.Source.Package);
        var proposal = index >= 0
            ? proposals[index] with { Source = request.Source, Title = title }
            : new PackageProposal(proposals.Count + 1, request.Source, title, author, ProposalStatus.Open, clock.GetUtcNow());
        if (index >= 0) { proposals[index] = proposal; }
        else if (proposals.Count(item => item.Status == ProposalStatus.Open) >= PackageRules.MaxOpenProposals)
        { throw new InvalidOperationException($"{id} already has {PackageRules.MaxOpenProposals} open proposals."); }
        else { proposals.Add(proposal); }
        var result = proposal.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Persist(id, Snapshot with { Proposals = proposals, Receipts = Receipted(request.OperationId, request, result) });
        return proposal;
    }

    public async Task<PackageSnapshot> Accept(AcceptProposal request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        RequireOwner(id);
        if (Replay(request.OperationId, request) is not null) { return Describe(id); }
        var index = Snapshot.Proposals.FindIndex(item => item.Number == request.Number);
        if (index < 0) { throw new KeyNotFoundException($"{id} has no proposal {request.Number}."); }
        var proposal = Snapshot.Proposals[index];
        if (proposal.Status != ProposalStatus.Open) { throw new InvalidOperationException($"Proposal {proposal.Number} is already {proposal.Status.ToString().ToLowerInvariant()}."); }
        var tip = proposal.Source.Revision;
        var arrived = await Fetch(proposal.Source);
        if (Snapshot.Head is { } head && !IsAncestor(head, tip, arrived))
        {
            throw new InvalidOperationException(
                $"Proposal {proposal.Number} is behind {id} at {head}. {proposal.Author} should merge {id}@{head} into {proposal.Source.Package} and propose again.");
        }
        var proposals = new List<PackageProposal>(Snapshot.Proposals) { [index] = proposal with { Status = ProposalStatus.Accepted } };
        await Persist(id, Advance(tip, arrived, request.OperationId, request, tip) with { Proposals = proposals });
        return Describe(id);
    }

    public async Task<PackageSnapshot> Publish(PublishPackage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Id;
        RequireOwner(id);
        if (Replay(request.OperationId, request) is null)
        {
            if (!Snapshot.Revisions.ContainsKey(request.Revision ?? "")) { throw new KeyNotFoundException($"{id} has no revision {request.Revision}."); }
            await Persist(id, Snapshot with { Published = request.Revision, Receipts = Receipted(request.OperationId, request, request.Revision!) });
        }
        await GrainFactory.GetGrain<IPackageDirectory>(PackageDirectory.Key).Refresh(id);
        return Describe(id);
    }

    private async Task VerifyArtifact(PackageContent content, CodeArtifactRef artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        VerifiedArtifact verified;
        try { verified = await artifacts.OpenVerifiedAsync(artifact, CancellationToken.None); }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException)
        { throw new ArgumentException("The artifact could not be verified: " + error.Message, error); }
        if (verified.Manifest.Source != content.Source || verified.Manifest.Tests != content.Tests)
        { throw new ArgumentException("The artifact was built from different source or tests than this revision. Check the exact content before committing it."); }
    }

    // Copies the revisions this package lacks from the source's lineage and checks each id against its content.
    private async Task<Dictionary<string, PackageRevision>> Fetch(PackageRevisionRef source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var remote = GrainFactory.GetGrain<IPackage>(source.Package.ToString());
        var arrived = new Dictionary<string, PackageRevision>();
        var pending = new Stack<string>([source.Revision]);
        while (pending.TryPop(out var revisionId))
        {
            if (Snapshot.Revisions.ContainsKey(revisionId) || arrived.ContainsKey(revisionId)) { continue; }
            if (Snapshot.Revisions.Count + arrived.Count >= PackageRules.MaxRevisions)
            { throw new InvalidOperationException($"A package keeps at most {PackageRules.MaxRevisions} revisions."); }
            var revision = await remote.ReadRevision(revisionId);
            if (revision.Id != revisionId || PackageHash.Revision(revision.Parents, revision.Content) != revisionId)
            { throw new InvalidDataException($"{source.Package} returned a revision that does not match its id {revisionId}."); }
            arrived[revisionId] = revision;
            foreach (var parent in revision.Parents) { pending.Push(parent); }
        }
        return arrived;
    }

    private bool IsAncestor(string ancestor, string descendant, Dictionary<string, PackageRevision> arrived)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([descendant]);
        while (pending.TryPop(out var revisionId))
        {
            if (revisionId == ancestor) { return true; }
            if (!seen.Add(revisionId)) { continue; }
            var revision = Snapshot.Revisions.GetValueOrDefault(revisionId) ?? arrived.GetValueOrDefault(revisionId);
            foreach (var parent in revision?.Parents ?? []) { pending.Push(parent); }
        }
        return false;
    }

    // Adds new revisions and moves the head. History lists revisions parents-first in the order they arrived.
    private PackageState Advance(string head, Dictionary<string, PackageRevision> arrived, Guid operationId, object request, string result)
    {
        var revisions = new Dictionary<string, PackageRevision>(Snapshot.Revisions);
        var history = new List<string>(Snapshot.History);
        void Add(string revisionId)
        {
            if (revisions.ContainsKey(revisionId) || !arrived.TryGetValue(revisionId, out var revision)) { return; }
            foreach (var parent in revision.Parents) { Add(parent); }
            revisions[revisionId] = revision;
            history.Add(revisionId);
        }
        foreach (var revisionId in arrived.Keys) { Add(revisionId); }
        if (revisions.Count > PackageRules.MaxRevisions)
        { throw new InvalidOperationException($"A package keeps at most {PackageRules.MaxRevisions} revisions. Fork it into a new package to continue."); }
        return Snapshot with { Head = head, Revisions = revisions, History = history, Receipts = Receipted(operationId, request, result) };
    }

    private string? Replay(Guid operationId, object request)
    {
        var receipt = Snapshot.Receipts.Find(item => item.OperationId == operationId);
        if (receipt is null) { return null; }
        return receipt.RequestHash == PackageHash.Of(request)
            ? receipt.Result
            : throw new InvalidOperationException($"Operation {operationId} was already used for a different change.");
    }

    private List<OperationReceipt> Receipted(Guid operationId, object request, string result)
    {
        var receipts = new List<OperationReceipt>(Snapshot.Receipts) { new(operationId, PackageHash.Of(request), result) };
        if (receipts.Count > PackageRules.MaxReceipts) { receipts.RemoveAt(0); }
        return receipts;
    }

    private PackageSnapshot Describe(PackageId id) => new(
        id,
        Snapshot.ForkedFrom,
        Snapshot.Head,
        Snapshot.Published,
        Snapshot.History.Select(revisionId => Snapshot.Revisions[revisionId])
            .Select(revision => new PackageRevisionSummary(revision.Id, revision.Parents, revision.Author, revision.Message, revision.CommittedAt)).ToArray(),
        Snapshot.Proposals.ToArray());

    private Task Persist(PackageId id, PackageState next) => Save(next, new PackageChanged(id, next.Head, next.Published));

    private static string RequireOwner(PackageId id)
    {
        var caller = RequireCaller();
        return caller == id.Owner ? caller : throw new UnauthorizedAccessException($"Only {id.Owner} can change {id}.");
    }

    private static string RequireCaller()
        => CallerContextStamper.TryGet(out var caller) && caller is not null && CallerContextStamper.IsTrusted(caller)
            && caller.Kind is CallerKind.User or CallerKind.Assistant
            ? caller.PrincipalId
            : throw new UnauthorizedAccessException("Sign in to change packages.");
}
