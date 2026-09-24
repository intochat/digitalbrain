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
        await VerifyArtifact(request.Content, request.Artifact);
        var revision = new PackageRevision(PackageHash.Revision(parents, request.Content), parents, request.Content, request.Artifact, author, message, clock.GetUtcNow());
        await Persist(id, Advance(revision.Id, new() { [revision.Id] = revision }, request.OperationId, request, revision.Id));
        return revision;
    }

    public Task<PackageSnapshot> Fork(ForkPackage request) => throw new NotSupportedException();

    public Task<PackageSnapshot> Pull(PullPackage request) => throw new NotSupportedException();

    public Task<PackageProposal> Propose(ProposeChange request) => throw new NotSupportedException();

    public Task<PackageSnapshot> Accept(AcceptProposal request) => throw new NotSupportedException();

    public Task<PackageSnapshot> Publish(PublishPackage request) => throw new NotSupportedException();

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

    private List<PackageReceipt> Receipted(Guid operationId, object request, string result)
    {
        var receipts = new List<PackageReceipt>(Snapshot.Receipts) { new(operationId, PackageHash.Of(request), result) };
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
