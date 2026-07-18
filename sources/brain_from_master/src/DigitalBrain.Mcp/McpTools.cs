using System.ComponentModel;
using System.Text.Json;
using DigitalBrain.Kernel.Contracts;
using DigitalBrain.Kernel.Contracts.Runtime;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;
using RuntimeRequestContext = DigitalBrain.Kernel.Contracts.Runtime.RequestContext;
namespace DigitalBrain.Mcp;

public sealed class McpAuthority(IHttpContextAccessor http, RuntimeRequestAuthenticator authentication, IConfiguration configuration)
{
    public async Task<RuntimeRequestContext> RequireContextAsync(CancellationToken cancellationToken = default)
    {
        _ = SessionAudiences.RequireFixedMcp(configuration["DigitalBrain:Runtime:Mcp:Audience"]);
        var httpContext = http.HttpContext;
        if (httpContext is null)
            throw new UnauthorizedAccessException("Authenticated MCP session required.");
        return await authentication.AuthenticateMcpAsync(httpContext, cancellationToken).ConfigureAwait(false)
               ?? throw new UnauthorizedAccessException("Authenticated MCP session required.");
    }
    internal static void DemandGrant(RuntimeRequestContext context, string grant)
    {
        if (!context.Grants.Contains(grant))
            throw new UnauthorizedAccessException("The authenticated principal lacks the required capability.");
    }
}
[McpServerToolType]
public sealed class McpConversationTools(McpAuthority authority, McpInoCommandHandler conversation, FeatureBuildEndpoint builds, FeatureLifecycleRail features)
{
    [McpServerTool(Name = "ino_interact"), Description("Durably accept an authenticated, idempotent INO interaction for the current workspace.")]
    public async Task<object> InoInteractAsync(string commandId, string prompt, CancellationToken cancellationToken = default)
    {
        var context = await authority.RequireContextAsync(cancellationToken).ConfigureAwait(false);
        McpAuthority.DemandGrant(context, "brain.interact");
        var commandContext = context with { IdempotencyKey = commandId };
        var receipt = await conversation.AcceptAsync(new CommandEnvelope(McpInoCommandHandler.CommandType, 2, commandId, commandContext, JsonSerializer.SerializeToElement(new { prompt }))).ConfigureAwait(false);
        return new
        {
            commandId,
            operationId = receipt.OperationId,
            phase = receipt.Phase.ToString(),
            receipt.Version
        };
    }
    [McpServerTool(Name = "feature_build"), Description("Build and verify one bounded Feature source snapshot through the isolated FeatureBuilder path.")]
    public async Task<object> BuildFeatureAsync(
        string implementationProjectPath,
        string scenarioProjectPath,
        IReadOnlyList<FeatureSourceInput> files,
        FeatureSourceKind sourceKind,
        CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        var artifact = await builds.BuildAsync(new FeatureBuildSubmission(implementationProjectPath, scenarioProjectPath, files, sourceKind), cancellationToken);
        return new
        {
            ownerId = context.OwnerId.Value,
            artifact.Release,
            artifact.Scenarios
        };
    }
    [McpServerTool(Name = "feature_propose"), Description("Create an exact-digest Feature approval with its complete provider connections and constraints.")]
    public async Task<object> ProposeFeatureAsync(
        string installationId,
        string releaseDigest,
        string sourceReference,
        FeatureSourceKind sourceKind,
        string[] requestedCapabilities,
        string[] dependencies,
        FeatureGrantSpec[] grants,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.ProposeAsync(
            context,
            new FeatureReleaseProposal(
                new FeatureInstallationId(installationId),
                new FeatureReleaseMetadata(new ReleaseDigest(releaseDigest), sourceReference, sourceKind, requestedCapabilities, dependencies),
                grants),
            expectedRevision,
            cancellationToken);
    }
    [McpServerTool(Name = "feature_decide"), Description("Approve or reject one exact Feature digest and grant set.")]
    public async Task<object> DecideFeatureAsync(string approvalId, string releaseDigest, bool approved, string decisionId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.DecideAsync(context, new FeatureApprovalDecision(approvalId, new ReleaseDigest(releaseDigest), approved, decisionId, context.ActorId), expectedRevision, cancellationToken);
    }
    [McpServerTool(Name = "feature_grant"), Description("Stage the exact grants already bound by an approved Feature digest.")]
    public async Task<object> GrantFeatureAsync(string installationId, string releaseDigest, FeatureGrantSpec[] grants, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.GrantAsync(context, new FeatureInstallationId(installationId), new ReleaseDigest(releaseDigest), grants, expectedRevision, cancellationToken);
    }
    [McpServerTool(Name = "feature_install"), Description("Install or update an approved Feature release and publish it for hot loading.")]
    public async Task<object> InstallFeatureAsync(string installationId, string releaseDigest, string[] subscriptions, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.InstallAsync(
            context,
            new FeatureInstallationRegistration(new FeatureInstallationId(installationId), new ReleaseDigest(releaseDigest), subscriptions),
            expectedRevision,
            cancellationToken);
    }
    [McpServerTool(Name = "feature_pause"), Description("Pause one Feature installation so the next claim and capability operation are denied.")]
    public async Task<object> PauseFeatureAsync(string installationId, string reason, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        await features.PauseAsync(context, new FeatureInstallationId(installationId), reason, expectedRevision, cancellationToken);
        return new { installationId, paused = true };
    }
    [McpServerTool(Name = "feature_revoke"), Description("Revoke one exact Feature capability grant so the next operation is denied.")]
    public async Task<object> RevokeFeatureGrantAsync(
        string installationId,
        string releaseDigest,
        string capabilityId,
        int capabilityVersion,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        await features.RevokeAsync(
            context,
            new FeatureGrantRevocation(new FeatureInstallationId(installationId), new ReleaseDigest(releaseDigest), capabilityId, capabilityVersion),
            expectedRevision,
            cancellationToken);
        return new { installationId, releaseDigest, capabilityId, capabilityVersion, revoked = true };
    }
    [McpServerTool(Name = "feature_resume"), Description("Resume one paused Feature installation.")]
    public async Task<object> ResumeFeatureAsync(string installationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        await features.ResumeAsync(context, new FeatureInstallationId(installationId), expectedRevision, cancellationToken);
        return new { installationId, paused = false };
    }
    [McpServerTool(Name = "feature_rollback"), Description("Roll one Feature installation back to its retained previous release.")]
    public async Task<object> RollbackFeatureAsync(string installationId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.RollbackAsync(context, new FeatureInstallationId(installationId), expectedRevision, cancellationToken);
    }
    [McpServerTool(Name = "feature_republish"), Description("Reconcile one active Feature installation into the hot-load discovery index.")]
    public async Task<object> RepublishFeatureAsync(string installationId, CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.RepublishAsync(context, new FeatureInstallationId(installationId), cancellationToken);
    }
    [McpServerTool(Name = "feature_inspect"), Description("Inspect Feature approvals, grants, installations, runtime state, and parked inputs.")]
    public async Task<object> InspectFeaturesAsync(CancellationToken cancellationToken = default)
    {
        var context = await FeatureContextAsync(cancellationToken);
        return await features.InspectAsync(context, cancellationToken);
    }
    private async Task<RuntimeRequestContext> FeatureContextAsync(CancellationToken cancellationToken)
    {
        var context = await authority.RequireContextAsync(cancellationToken).ConfigureAwait(false);
        McpAuthority.DemandGrant(context, "feature.manage");
        return context;
    }
}
