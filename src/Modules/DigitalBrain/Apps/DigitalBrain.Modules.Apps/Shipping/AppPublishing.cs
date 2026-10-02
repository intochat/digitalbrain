using System.Text.Json;
using DigitalBrain.Contracts;

namespace DigitalBrain.Apps;

internal sealed class AppPublishing(IDigitalBrain brain, AppAuthoringPolicy policy)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AppVerification> Publish(PackageId id, PackageContent content, string message)
    {
        var package = brain.Get<IPackage>(id.ToString());
        var snapshot = await package.Read();
        var revisionId = snapshot.Head;
        if (revisionId is null || Canonical((await package.ReadRevision(revisionId)).Content) != Canonical(content))
        { revisionId = (await package.Commit(new(Guid.NewGuid(), snapshot.Head, content, message))).Id; }
        var reference = new PackageRevisionRef(id, revisionId);
        var verification = await Verify(reference);
        if (verification.Green && snapshot.Published != revisionId)
        { await package.Publish(new(Guid.NewGuid(), revisionId)); }
        return verification;
    }

    public async Task<AppVerification> Verify(PackageRevisionRef reference, bool force = false)
    {
        await RequireRunnable(reference);
        var verifier = brain.Get<IAppVerification>(IAppVerification.Key(reference));
        return (!force ? await verifier.Read() : null) ?? await verifier.Verify();
    }

    public async Task RequireRunnable(PackageRevisionRef revision)
    {
        var content = (await brain.Get<IPackage>(revision.Package.ToString()).ReadRevision(revision.Revision)).Content;
        if (content.Manifest.RuntimeName == PackageManifest.CSharpRuntime || content.File(PackageContent.TestsPath) is not null)
        { policy.RequireSandbox(); }
    }

    private static string Canonical(PackageContent content) => JsonSerializer.Serialize(new
    {
        content.Manifest,
        content.Source,
        files = new SortedDictionary<string, string>(content.Files?.ToDictionary() ?? [], StringComparer.Ordinal),
    }, Json);
}
