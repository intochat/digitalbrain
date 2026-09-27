using DigitalBrain.Apps;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Packages;

// Publishes a workspace C# file as a package owned by the signed-in person.
internal sealed class CSharpSharing(IDigitalBrain brain, CSharpToolService files, IOptions<BasicAuthOptions> auth)
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 2000;

    public async Task<PackageSnapshot> Share(string workspaceId, string fileId, ShareCSharpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var workspace = files.ForScope(WorkspaceScope.Current(auth.Value, workspaceId).Id);
        var description = await workspace.Description(fileId, cancellationToken);
        var file = await workspace.ReadFile(fileId, cancellationToken);
        if (string.IsNullOrWhiteSpace(file.Source)) { throw new InvalidOperationException($"{fileId} has no source to share."); }
        var manifest = new PackageManifest(Shorten(description.Name, MaxTitleLength), Shorten(description.Purpose, MaxDescriptionLength), [], [], request.Accounts ?? []);
        var content = new PackageContent(manifest, file.Source);
        var package = brain.Get<IPackage>(PackageId.Create(CallerContextStamper.Require().PrincipalId, request.Name ?? PackageName(fileId)).ToString());
        var head = (await package.Read()).Head;
        var revision = head is not null && SameContent((await package.ReadRevision(head)).Content, content)
            ? head
            : (await package.Commit(new(Guid.NewGuid(), head, content, request.Message ?? "Shared from " + manifest.Title))).Id;
        return await package.Publish(new(Guid.NewGuid(), revision));
    }

    private static bool SameContent(PackageContent published, PackageContent candidate)
        => published.Source == candidate.Source && published.Manifest.Title == candidate.Manifest.Title
            && published.Manifest.Description == candidate.Manifest.Description
            && (published.Manifest.Accounts ?? []).SequenceEqual(candidate.Manifest.Accounts ?? []);

    private static string PackageName(string fileId) => Shorten(fileId.ToLowerInvariant().Replace('_', '-').TrimStart('-'), 64);

    private static string Shorten(string value, int length) => value.Length <= length ? value : value[..length];
}
