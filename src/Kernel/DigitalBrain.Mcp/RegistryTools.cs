using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Auth;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class RegistryTools(IDigitalBrain brain, IHttpContextAccessor httpContextAccessor)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = McpSurface.ListRegistry)]
    [Description("List registered instances for the authenticated caller's principal partition.")]
    public async Task<IReadOnlyList<RegistryEntry>> ListRegistryAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Bound);

        var registryName = McpActor.Partition(actor, IRegistry.InstanceName);
        var listed = await brain
            .Get<IRegistry>(registryName)
            .FireAsync<InstancesListed>(new ListInstances(CommandId.New()), timeout.Token)
            .ConfigureAwait(false);

        return
        [
            .. listed.Items
                .OrderBy(static e => e.Subject.ToString(), StringComparer.Ordinal)
                .Select(static e => new RegistryEntry(
                    e.Subject.ToString(),
                    e.Role,
                    e.Bundle,
                    e.Enabled,
                    e.Note,
                    e.RegisteredAt)),
        ];
    }

    [McpServerTool(Name = McpSurface.RegisterInstance)]
    [Description(
        "Register an instance in the authenticated caller's principal partition. "
        + "localName is the short name (e.g. sales); grainType is chart|timer|…")]
    public async Task<RegistryEntry> RegisterInstanceAsync(
        [Description("Grain type, e.g. chart or timer")] string grainType,
        [Description("Local instance name without principal prefix, e.g. sales")] string localName,
        [Description("Role label: chart, schedule, …")] string role,
        [Description("Optional note")] string? note = null,
        [Description("Whether enabled")] bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var instanceName = McpActor.Partition(actor, localName.Trim());
        var subject = new NeuronId(grainType.Trim(), brain.Owner, instanceName);
        var registryName = McpActor.Partition(actor, IRegistry.InstanceName);

        var registered = await brain
            .Get<IRegistry>(registryName)
            .FireAsync<InstanceRegistered>(
                new RegisterInstance(CommandId.New(), subject, role.Trim(), Bundle: null, enabled, note),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        var instance = registered.Instance;
        return new RegistryEntry(
            instance.Subject.ToString(),
            instance.Role,
            instance.Bundle,
            instance.Enabled,
            instance.Note,
            instance.RegisteredAt);
    }

    [McpServerTool(Name = McpSurface.InstallBundle)]
    [Description("Install a disabled bundle into the authenticated caller's registry partition.")]
    public async Task<BundleInstallResult> InstallBundleAsync(
        [Description("Bundle name")] string name,
        [Description("JSON array of members: [{grainType,name,role,note?},…] — name is local")] string membersJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(membersJson);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var members = System.Text.Json.JsonSerializer.Deserialize<BundleMemberDto[]>(
                membersJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("membersJson must be a JSON array.");

        if (members.Length == 0)
        {
            throw new ArgumentException("Bundle needs at least one member.");
        }

        var registryName = McpActor.Partition(actor, IRegistry.InstanceName);
        var installed = await brain
            .Get<IRegistry>(registryName)
            .FireAsync<BundleInstalled>(
                new InstallBundle(
                    CommandId.New(),
                    name.Trim(),
                    [.. members.Select(m => new BundleMember(
                        m.GrainType ?? throw new ArgumentException("grainType required"),
                        McpActor.Partition(actor, m.Name ?? throw new ArgumentException("name required")),
                        m.Role ?? throw new ArgumentException("role required"),
                        m.Note))],
                    Wires: [],
                    Intent: $"mcp install_bundle '{name.Trim()}'"),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return new BundleInstallResult(
            installed.Name,
            installed.MemberCount,
            installed.WireCount,
            installed.Enabled);
    }

    [McpServerTool(Name = McpSurface.GrantAccess)]
    [Description(
        "Grant read access on a subject owned by the authenticated caller to another principal "
        + "(granteePrincipalId is a GUID).")]
    public async Task<string> GrantAccessAsync(
        [Description("Grantee principal id (GUID)")] string granteePrincipalId,
        [Description("Subject grain type, e.g. chart")] string subjectType,
        [Description("Subject local name owned by the caller, e.g. sales")] string subjectLocalName,
        [Description("Intent for the grant")] string? intent = null,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        var grantee = McpActor.ParsePrincipalId(granteePrincipalId, nameof(granteePrincipalId));
        using var _ = VerifiedActor.Enter(actor);

        var subject = new NeuronId(
            subjectType.Trim(),
            brain.Owner,
            McpActor.Partition(actor, subjectLocalName.Trim()));
        var grantsName = McpActor.Partition(actor, IGrants.InstanceName);

        var granted = await brain
            .Get<IGrants>(grantsName)
            .FireAsync<AccessGranted>(
                new GrantAccess(CommandId.New(), grantee, subject, GrantKind.Read, intent),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"Granted {granted.Grant.Kind} on {granted.Grant.Subject} to {granted.Grant.Grantee.Value:N}";
    }

    [McpServerTool(Name = McpSurface.RevokeAccess)]
    [Description("Revoke a previously granted read access on a subject owned by the authenticated caller.")]
    public async Task<string> RevokeAccessAsync(
        [Description("Grantee principal id (GUID)")] string granteePrincipalId,
        [Description("Subject grain type")] string subjectType,
        [Description("Subject local name")] string subjectLocalName,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        var grantee = McpActor.ParsePrincipalId(granteePrincipalId, nameof(granteePrincipalId));
        using var _ = VerifiedActor.Enter(actor);

        var subject = new NeuronId(
            subjectType.Trim(),
            brain.Owner,
            McpActor.Partition(actor, subjectLocalName.Trim()));
        var grantsName = McpActor.Partition(actor, IGrants.InstanceName);

        var revoked = await brain
            .Get<IGrants>(grantsName)
            .FireAsync<AccessRevoked>(
                new RevokeAccess(CommandId.New(), grantee, subject, GrantKind.Read),
                cancellationToken)
            .WaitAsync(Bound, cancellationToken)
            .ConfigureAwait(false);

        return $"Revoked {revoked.Kind} on {revoked.Subject} from {revoked.Grantee.Value:N}";
    }

    [McpServerTool(Name = McpSurface.ReadChart)]
    [Description(
        "Read a chart as the authenticated caller. subjectLocalName is the chart's local name; "
        + "ownerPrincipalId defaults to the caller when omitted.")]
    public async Task<string> ReadChartAsync(
        [Description("Chart local name, e.g. sales")] string subjectLocalName,
        [Description("Owner principal id (GUID); omit to use the caller")] string? ownerPrincipalId = null,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        var owner = string.IsNullOrWhiteSpace(ownerPrincipalId)
            ? actor.PrincipalId
            : McpActor.ParsePrincipalId(ownerPrincipalId, nameof(ownerPrincipalId));
        using var _ = VerifiedActor.Enter(actor);

        var chartName = PrincipalPartition.InstanceName(owner, subjectLocalName.Trim());
        try
        {
            var points = await brain
                .GetGrainProxy<IChart>(chartName)
                .Read()
                .WaitAsync(Bound, cancellationToken)
                .ConfigureAwait(false);
            return $"OK count={points.Count} chart=chart:{brain.Owner.Value}/{chartName}";
        }
        catch (NeuronAuthorizationException refused)
        {
            return $"DENIED {refused.Message}";
        }
        catch (Exception ex)
        {
            if (ex.InnerException is NeuronAuthorizationException inner)
            {
                return $"DENIED {inner.Message}";
            }

            return $"ERROR {ex.GetType().Name}: {ex.Message}";
        }
    }
}
