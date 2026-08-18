using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.UI;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class GrantChartTools(IDigitalBrain brain)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

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
