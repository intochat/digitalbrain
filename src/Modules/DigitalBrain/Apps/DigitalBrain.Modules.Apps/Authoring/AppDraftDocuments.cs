using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Apps;

internal sealed partial class AppDraftNeuron
{
    public Task<AppAuthoringDocument> ProposeConversion(long expectedRevision)
    {
        RequireRevision(expectedRevision);
        RequireSpec();
        if (Snapshot.Document is not null) { throw new InvalidOperationException("This draft already has behavior blocks."); }
        return Task.FromResult(AppDocumentConversion.Propose(Snapshot.Spec));
    }

    private void RequireOwner()
    {
        if (CallerContextStamper.Require().PrincipalId != Owner)
        { throw new UnauthorizedAccessException("This draft belongs to another person."); }
    }

    private void RequireRevision(long expected)
    {
        RequireOwner();
        if (Snapshot.Revision != expected || Snapshot.Status == AppDraftStatus.Building)
        { throw new AppDraftConflictException("This draft changed. Reload it before saving your edits."); }
    }

    public async Task<AppDraftView> SaveDocument(SaveAppDocument request)
    {
        RequireRevision(request.ExpectedRevision);
        RequireSpec();
        AppDocumentCodec.Validate(request.Document);
        await Persist(Snapshot with { Document = request.Document, Spec = AppDocumentCodec.ExportSpec(request.Document), VerifiedDocumentHash = null, Status = AppDraftStatus.Drafted, Error = "" });
        return await Read();
    }

    public async Task<AppDraftView> ImportRevision(PackageRevisionRef revision, long expectedRevision)
    {
        RequireRevision(expectedRevision);
        if (Snapshot.Revision != 0) { throw new AppDraftConflictException("Import requires a new draft."); }
        if (revision.Package.Owner != Owner) { throw new UnauthorizedAccessException("Customize a copy of this app before editing it."); }
        var content = (await GrainFactory.GetGrain<IPackage>(revision.Package.ToString()).ReadRevision(revision.Revision)).Content;
        var read = AppDocumentCodec.Read(content);
        if (read.Error is not null) { throw new ArgumentException(read.Error); }
        await Persist(Snapshot with { Name = revision.Package.Name, Title = content.Manifest.Title, Description = content.Manifest.Description, Runtime = content.Manifest.RuntimeName, Request = content.Manifest.Description, Spec = read.OriginalSpec, Document = read.Document, SourceRevision = revision, Status = AppDraftStatus.Drafted });
        return await Read();
    }
}
