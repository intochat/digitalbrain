using DigitalBrain.AI.GroupChat;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.Core.Enforcement;

namespace DigitalBrain.Apps;

internal sealed class MarketplaceService(IDigitalBrain brain, AppPublishing publishing, IContractVocabulary? vocabulary = null)
{

    // Verifying any revision runs its tests.cs in the sandbox, so it is allowed exactly when running scripts is.
    public Task RequireRunnable(PackageRevisionRef revision) => publishing.RequireRunnable(revision);

    public async Task<AppSpecView> Spec(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        var content = (await brain.Get<IPackage>(id.ToString()).ReadRevision(revision.Revision)).Content;
        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(revision)).Read();
        var files = new Dictionary<string, string>(content.Files ?? new Dictionary<string, string>());
        if (!string.IsNullOrEmpty(content.Source)) { files[PackageContent.SourcePath] = content.Source; }
        return new(revision, content.Manifest.RuntimeName, files,
            content.File(PackageContent.SpecPath), content.File(PackageContent.TestsPath), verification,
            content.Manifest, AppDocumentCodec.Read(content), AppSpecVocabulary.Read(content.Manifest, vocabulary),
            CallerContextStamper.Require().PrincipalId == id.Owner);
    }

    public async Task<AppSpecView> Verify(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        await publishing.Verify(revision, force: true);
        return await Spec(id, revision.Revision);
    }

    public Task<GroupChatState> Discussion(string appKey, Guid invocationId)
        => brain.Get<IGroupChat>(GroupChatRuntime.ChatKey(appKey, invocationId)).Read();

    private async Task<PackageRevisionRef> Revision(PackageId id, string? revisionId)
    {
        if (revisionId is not null) { return new(id, revisionId); }
        var snapshot = await brain.Get<IPackage>(id.ToString()).Read();
        return new(id, snapshot.Head ?? throw new KeyNotFoundException($"{id} has no revisions yet."));
    }
}
