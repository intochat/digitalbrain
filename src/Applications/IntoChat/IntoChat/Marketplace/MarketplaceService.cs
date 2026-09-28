using DigitalBrain.Microsoft.CSharp;
using DigitalBrain.AI.GroupChat;
using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Specs;

namespace IntoChat.Marketplace;

// What the marketplace shows about an app beyond its listing: its scenarios bound to the brain's steps,
// with the verdicts of the revision's last verification.
internal sealed record AppSpecView(
    PackageRevisionRef Revision,
    string Runtime,
    IReadOnlyDictionary<string, string> Files,
    FeatureSnapshot? Feature,
    AppVerification? Verification);

internal sealed class MarketplaceService(IDigitalBrain brain, CSharpToolService csharp)
{
    public const string ActivationDisabled = "Running C# apps is disabled by host policy, and verifying one runs it. Enable DigitalBrain:CSharp:AllowActivation.";

    // Verifying a csharp revision runs its script, so it is allowed exactly when installing one is.
    public async Task RequireRunnable(PackageRevisionRef revision)
    {
        var runtime = (await brain.Get<IPackage>(revision.Package.ToString()).ReadRevision(revision.Revision)).Content.Manifest.RuntimeName;
        if (runtime == PackageManifest.CSharpRuntime && !csharp.AllowActivation) { throw new InvalidOperationException(ActivationDisabled); }
    }

    public async Task<AppSpecView> Spec(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        var content = (await brain.Get<IPackage>(id.ToString()).ReadRevision(revision.Revision)).Content;
        FeatureSnapshot? feature = null;
        if (content.File(PackageContent.SpecPath) is { } spec)
        {
            // Setting the same text again is a no-op, so this only binds a spec nobody verified yet.
            feature = await brain.Get<IFeature>(IAppVerification.FeatureKey(revision)).Set(spec);
        }
        var verification = await brain.Get<IAppVerification>(IAppVerification.Key(revision)).Read();
        return new(revision, content.Manifest.RuntimeName, content.Files ?? new Dictionary<string, string>(), feature, verification);
    }

    public async Task<AppSpecView> Verify(PackageId id, string? revisionId)
    {
        var revision = await Revision(id, revisionId);
        await RequireRunnable(revision);
        await brain.Get<IAppVerification>(IAppVerification.Key(revision)).Verify();
        return await Spec(id, revision.Revision);
    }

    public Task<IReadOnlyList<StepPattern>> Vocabulary() => brain.Get<IFeature>("vocabulary").Vocabulary();

    public Task<GroupChatState> Discussion(string appKey, Guid invocationId)
        => brain.Get<IGroupChat>(GroupChatRuntime.ChatKey(appKey, invocationId)).Read();

    private async Task<PackageRevisionRef> Revision(PackageId id, string? revisionId)
    {
        if (revisionId is not null) { return new(id, revisionId); }
        var snapshot = await brain.Get<IPackage>(id.ToString()).Read();
        return new(id, snapshot.Head ?? throw new KeyNotFoundException($"{id} has no revisions yet."));
    }
}
