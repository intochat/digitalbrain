using System.Text.Json;
using DigitalBrain.Apps;
using DigitalBrain.Behavior;
using DigitalBrain.Coding;
using DigitalBrain.Contracts;
using DigitalBrain.Core.Enforcement;
using IntoChat.Workspace;
using Microsoft.Extensions.Options;

namespace IntoChat.Packages;

// Turns an automation from the behavior console into a published package owned by the signed-in
// person. The draft's passing check vouches for the revision, so sharing never rebuilds.
internal sealed class BehaviorSharing(IDigitalBrain brain, BehaviorToolService behaviors, IOptions<BasicAuthOptions> auth)
{
    private const int MaxTitleLength = 100;
    private const int MaxDescriptionLength = 2000;
    private const string SettingPrefix = "Behavior__";

    public async Task<PackageSnapshot> Share(string workspaceId, string behaviorId, ShareBehaviorRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var automation = behaviors.ForScope(WorkspaceScope.Current(auth.Value, workspaceId).Id);
        var draft = await automation.ReadDraft(behaviorId, cancellationToken);
        var check = draft.LatestCheckId is { } latest ? await automation.ReadCheck(behaviorId, latest, cancellationToken) : null;
        if (check is not { Status: CodeCheckStatus.Passed, Artifact: { } artifact } || check.Revision != draft.Revision)
        { throw new InvalidOperationException("Check the latest draft before sharing it; only code that passed its tests can be shared."); }
        var description = await automation.Description(behaviorId, cancellationToken);
        var manifest = new PackageManifest(
            Shorten(description.Name, MaxTitleLength),
            Shorten(description.Purpose, MaxDescriptionLength),
            [],
            DeployedSettings(await automation.ReadBehavior(behaviorId, cancellationToken)));
        var content = new PackageContent(manifest, draft.Source, draft.Tests, draft.ModuleIds);
        var package = brain.Get<IPackage>(PackageId.Create(CallerContextStamper.Require().PrincipalId, request.Name ?? PackageName(behaviorId)).ToString());
        var head = (await package.Read()).Head;
        var revision = head is not null && SameContent((await package.ReadRevision(head)).Content, content)
            ? head
            : (await package.Commit(new(Guid.NewGuid(), head, content, artifact, request.Message ?? "Shared from " + manifest.Title))).Id;
        return await package.Publish(new(Guid.NewGuid(), revision));
    }

    // The automation's current configuration becomes the package's settings, defaulting to the shared values.
    private static PackageSetting[] DeployedSettings(BehaviorSnapshot program)
    {
        var deployment = program.Deployments.LastOrDefault(item => item.Revision == program.DesiredDeploymentRevision);
        if (deployment is null) { return []; }
        using var configuration = JsonDocument.Parse(deployment.ConfigurationJson);
        return configuration.RootElement.EnumerateObject()
            .Where(property => property.Name.StartsWith(SettingPrefix, StringComparison.Ordinal))
            .Select(property => new PackageSetting(property.Name[SettingPrefix.Length..], "The value it ran with when it was shared.", property.Value.GetString() ?? ""))
            .ToArray();
    }

    private static bool SameContent(PackageContent published, PackageContent candidate)
        => JsonSerializer.Serialize(published) == JsonSerializer.Serialize(candidate);

    private static string PackageName(string behaviorId)
    {
        var name = behaviorId.ToLowerInvariant().Replace('_', '-').TrimStart('-');
        return Shorten(name, 64);
    }

    private static string Shorten(string value, int length) => value.Length <= length ? value : value[..length];
}
