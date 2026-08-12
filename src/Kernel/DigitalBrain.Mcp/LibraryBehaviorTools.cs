using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class LibraryBehaviorTools(IDigitalBrain brain, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(90);

    [McpServerTool(Name = McpSurface.PublishLibrary)]
    [Description("Publish an immutable library artifact (content-hashed) as the authenticated caller.")]
    public async Task<string> PublishLibraryAsync(
        [Description("Artifact name")] string name,
        [Description("Version, e.g. 1.0.0")] string version,
        [Description("Description for discover")] string description,
        [Description("JSON structure with members array and optional numbers map")] string structureJson,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var published = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryArtifactPublished>(
                new PublishLibraryArtifact(
                    CommandId.New(), name, version, description, structureJson, actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var a = published.Artifact;
        return $"PUBLISHED id={a.ArtifactId} name={a.Name}@{a.Version} hash={a.ContentHash} publisher={a.Publisher.Value:N}";
    }

    [McpServerTool(Name = McpSurface.DiscoverLibrary)]
    [Description("Discover published library artifacts by intent.")]
    public async Task<string> DiscoverLibraryAsync(
        [Description("Search intent")] string intent,
        [Description("Max hits")] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var page = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryDiscoveries>(
                new DiscoverLibrary(CommandId.New(), intent, limit),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        if (page.Artifacts.Length == 0)
        {
            return "No library artifacts matched.";
        }

        return string.Join(
            "\n",
            page.Artifacts.Select(a =>
                $"{a.ArtifactId} | {a.Name}@{a.Version} | {a.Description} | hash={a.ContentHash[..12]}…"));
    }

    [McpServerTool(Name = McpSurface.InstallLibrary)]
    [Description("Install a library artifact into the authenticated caller's principal partition (disabled).")]
    public async Task<string> InstallLibraryAsync(
        [Description("Artifact id from discover")] string artifactId,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var recorded = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryInstallRecorded>(
                new InstallLibraryArtifact(CommandId.New(), artifactId, actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var i = recorded.Install;
        return $"INSTALLED installId={i.InstallId} artifact={i.ArtifactId} enabled={i.Enabled} installer={i.Installer.Value:N}";
    }

    [McpServerTool(Name = McpSurface.EnableLibraryInstall)]
    [Description("Enable the authenticated caller's library install with optional config JSON (e.g. numbers).")]
    public async Task<string> EnableLibraryInstallAsync(
        [Description("Install id")] string installId,
        [Description("Optional principal-local config JSON")] string? configJson = null,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var enabled = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryInstallEnabled>(
                new EnableLibraryInstall(CommandId.New(), installId, configJson, actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var i = enabled.Install;
        return $"ENABLED installId={i.InstallId} config={i.ConfigJson ?? "{}"} enabled={i.Enabled}";
    }

    [McpServerTool(Name = McpSurface.ListLibraryInstalls)]
    [Description("List library installs for the authenticated caller.")]
    public async Task<string> ListLibraryInstallsAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var listed = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryInstallsListed>(
                new ListLibraryInstalls(CommandId.New(), actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        if (listed.Installs.Length == 0)
        {
            return "No installs.";
        }

        return string.Join(
            "\n",
            listed.Installs.Select(i =>
                $"{i.InstallId} | {i.Name}@{i.Version} enabled={i.Enabled} config={i.ConfigJson ?? "-"}"));
    }

    [McpServerTool(Name = McpSurface.StartRepoReview)]
    [Description(
        "Wave 8: open a repo root, stance up to maxFiles, run moderator rounds, return plan summary.")]
    public async Task<string> StartRepoReviewAsync(
        [Description("Absolute repo root path")] string rootPath,
        [Description("Change intent")] string intent = "improve reliability",
        [Description("Max files to stance")] int maxFiles = 30,
        [Description("Moderator rounds")] int moderatorRounds = 3,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var started = await brain
            .Get<IBehavior>(IBehavior.InstanceName)
            .FireAsync<BehaviorRunStarted>(
                new StartRepoReview(CommandId.New(), rootPath, intent, maxFiles, moderatorRounds),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var s = started.Summary;
        return $"RUN id={s.RunId} status={s.Status} files={s.FileCount} stances={s.StanceCount} "
            + $"rounds={s.ModeratorRounds} intent={s.Intent}";
    }

    [McpServerTool(Name = McpSurface.ReadBehaviorRun)]
    [Description("Read a completed behavior run: stances, moderator rounds, and written plan.")]
    public async Task<string> ReadBehaviorRunAsync(
        [Description("Run id from start_repo_review")] string runId,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var snap = await brain
            .Get<IBehavior>(IBehavior.InstanceName)
            .FireAsync<BehaviorRunSnapshot>(
                new ReadBehaviorRun(CommandId.New(), runId),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var stancePreview = string.Join(
            "; ",
            snap.Stances.Take(5).Select(s => $"{s.RelativePath}:{s.Stance}"));
        var planHead = snap.Plan.Length > 400 ? snap.Plan[..400] + "…" : snap.Plan;
        return $"status={snap.Summary.Status} stances={snap.Stances.Length} rounds={snap.Rounds.Length}\n"
            + $"sample={stancePreview}\n--- plan ---\n{planHead}";
    }

    [McpServerTool(Name = McpSurface.ReadInstallConfig)]
    [Description("Read enabled install config for the authenticated caller (shows principal-local numbers).")]
    public async Task<string> ReadInstallConfigAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var listed = await brain
            .Get<ILibrary>(ILibrary.InstanceName)
            .FireAsync<LibraryInstallsListed>(
                new ListLibraryInstalls(CommandId.New(), actor),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var enabled = listed.Installs.Where(i => i.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return "No enabled installs.";
        }

        return string.Join(
            "\n",
            enabled.Select(i =>
            {
                var numbers = "-";
                if (!string.IsNullOrWhiteSpace(i.ConfigJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(i.ConfigJson);
                        if (doc.RootElement.TryGetProperty("numbers", out var n))
                        {
                            numbers = n.GetRawText();
                        }
                    }
                    catch (JsonException)
                    {
                        numbers = i.ConfigJson;
                    }
                }

                return $"{i.Name}@{i.Version} numbers={numbers} hash={i.ContentHash[..12]}";
            }));
    }
}
