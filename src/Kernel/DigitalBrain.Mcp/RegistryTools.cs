using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class RegistryTools(IDigitalBrain brain)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    [McpServerTool(Name = McpSurface.ListRegistry)]
    [Description("List registered instances for a principal partition (alice|bob|operator).")]
    public async Task<IReadOnlyList<RegistryEntry>> ListRegistryAsync(
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Bound);

        var registryName = PrincipalPartition.InstanceName(principal, IRegistry.InstanceName);
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
        "Register a principal-scoped instance. localName is the short name (e.g. sales); "
        + "grainType is chart|timer|…; principalKey scopes the instance.")]
    public async Task<RegistryEntry> RegisterInstanceAsync(
        [Description("Grain type, e.g. chart or timer")] string grainType,
        [Description("Local instance name without principal prefix, e.g. sales")] string localName,
        [Description("Role label: chart, schedule, …")] string role,
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
        [Description("Optional note")] string? note = null,
        [Description("Whether enabled")] bool enabled = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(localName);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));

        var instanceName = PrincipalPartition.InstanceName(principal, localName.Trim());
        var subject = new NeuronId(grainType.Trim(), brain.Owner, instanceName);
        var registryName = PrincipalPartition.InstanceName(principal, IRegistry.InstanceName);

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
    [Description("Install a disabled bundle into a principal's registry partition.")]
    public async Task<BundleInstallResult> InstallBundleAsync(
        [Description("Bundle name")] string name,
        [Description("JSON array of members: [{grainType,name,role,note?},…] — name is local")] string membersJson,
        [Description("Principal key: operator, alice, or bob")] string principalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(membersJson);

        var (principal, username) = ChatTools.ResolvePrincipal(principalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(principal, username));

        var members = System.Text.Json.JsonSerializer.Deserialize<BundleMemberDto[]>(
                membersJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new ArgumentException("membersJson must be a JSON array.");

        if (members.Length == 0)
        {
            throw new ArgumentException("Bundle needs at least one member.");
        }

        var registryName = PrincipalPartition.InstanceName(principal, IRegistry.InstanceName);
        var installed = await brain
            .Get<IRegistry>(registryName)
            .FireAsync<BundleInstalled>(
                new InstallBundle(
                    CommandId.New(),
                    name.Trim(),
                    [.. members.Select(m => new BundleMember(
                        m.GrainType ?? throw new ArgumentException("grainType required"),
                        PrincipalPartition.InstanceName(principal, m.Name ?? throw new ArgumentException("name required")),
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
    [Description("Grant read access on a principal-owned subject to another principal (alice|bob|operator).")]
    public async Task<string> GrantAccessAsync(
        [Description("Grantor principal key")] string grantorKey,
        [Description("Grantee principal key")] string granteeKey,
        [Description("Subject grain type, e.g. chart")] string subjectType,
        [Description("Subject local name owned by grantor, e.g. sales")] string subjectLocalName,
        [Description("Intent for the grant")] string? intent = null,
        CancellationToken cancellationToken = default)
    {
        var (grantor, grantorName) = ChatTools.ResolvePrincipal(grantorKey);
        var (grantee, _) = ChatTools.ResolvePrincipal(granteeKey);
        using var _ = VerifiedActor.Enter(new ActorContext(grantor, grantorName));

        var subject = new NeuronId(
            subjectType.Trim(),
            brain.Owner,
            PrincipalPartition.InstanceName(grantor, subjectLocalName.Trim()));
        var grantsName = PrincipalPartition.InstanceName(grantor, IGrants.InstanceName);

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
    [Description("Revoke a previously granted read access.")]
    public async Task<string> RevokeAccessAsync(
        [Description("Grantor principal key")] string grantorKey,
        [Description("Grantee principal key")] string granteeKey,
        [Description("Subject grain type")] string subjectType,
        [Description("Subject local name")] string subjectLocalName,
        CancellationToken cancellationToken = default)
    {
        var (grantor, grantorName) = ChatTools.ResolvePrincipal(grantorKey);
        var (grantee, _) = ChatTools.ResolvePrincipal(granteeKey);
        using var _ = VerifiedActor.Enter(new ActorContext(grantor, grantorName));

        var subject = new NeuronId(
            subjectType.Trim(),
            brain.Owner,
            PrincipalPartition.InstanceName(grantor, subjectLocalName.Trim()));
        var grantsName = PrincipalPartition.InstanceName(grantor, IGrants.InstanceName);

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
        "Read a chart as a principal. subjectLocalName is the chart's local name; "
        + "ownerPrincipalKey is who owns the chart partition; readerPrincipalKey is who reads.")]
    public async Task<string> ReadChartAsync(
        [Description("Chart local name, e.g. sales")] string subjectLocalName,
        [Description("Owner principal key of the chart")] string ownerPrincipalKey,
        [Description("Reader principal key")] string readerPrincipalKey = "operator",
        CancellationToken cancellationToken = default)
    {
        var (ownerPrincipal, _) = ChatTools.ResolvePrincipal(ownerPrincipalKey);
        var (reader, readerName) = ChatTools.ResolvePrincipal(readerPrincipalKey);
        using var _ = VerifiedActor.Enter(new ActorContext(reader, readerName));

        var chartName = PrincipalPartition.InstanceName(ownerPrincipal, subjectLocalName.Trim());
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
            // Orleans may wrap settled refusals.
            if (ex.InnerException is NeuronAuthorizationException inner)
            {
                return $"DENIED {inner.Message}";
            }

            return $"ERROR {ex.GetType().Name}: {ex.Message}";
        }
    }
}
