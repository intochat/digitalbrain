using DigitalBrain.AI.GroupChat;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

internal sealed class MarketplaceService(IDigitalBrain brain, AppAuthoringPolicy policy)
{

    // Verifying any revision runs its tests.cs in the sandbox, so it is allowed exactly when running scripts is.
    public async Task RequireRunnable(PackageRevisionRef revision)
    {
        var content = (await brain.Get<IPackage>(revision.Package.ToString()).ReadRevision(revision.Revision)).Content;
        var runsScripts = content.Manifest.RuntimeName == PackageManifest.CSharpRuntime || content.File(PackageContent.TestsPath) is not null;
        if (runsScripts) { policy.RequireSandbox(); }
    }

    public async Task<AppSpecView> Spec(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        var content = (await brain.Get<IPackage>(id.ToString()).ReadRevision(revision.Revision)).Content;
        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(revision)).Read();
        return new(revision, content.Manifest.RuntimeName, content.Files ?? new Dictionary<string, string>(),
            content.File(PackageContent.SpecPath), content.File(PackageContent.TestsPath), verification);
    }

    public async Task<AppSpecView> Verify(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        await RequireRunnable(revision);
        await brain.Get<IAppVerification>(IAppVerification.Key(revision)).Verify();
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
