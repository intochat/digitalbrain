using DigitalBrain.Contracts.Enforcement;
using Orleans;

namespace DigitalBrain.Identity.Grants;

// The call filter only knows caller context; the grant stage reads membership and grants through
// this seam so the rule stays a pure function and the source can be faked in tests.
internal interface IGrantPolicySource
{
    ValueTask<Member?> FindMemberAsync(CallerContext caller, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken);

    ValueTask ConsumeOnceAsync(CallerContext caller, IReadOnlyList<Grant> grants, CancellationToken cancellationToken);
}

internal sealed class GrainGrantPolicySource(IGrainFactory grains) : IGrantPolicySource
{
    public ValueTask<Member?> FindMemberAsync(CallerContext caller, CancellationToken cancellationToken)
        => new(grains.GetGrain<IIdentityDirectory>(IdentityGrains.Directory)
            .FindMemberAsync(caller.PrincipalId, cancellationToken));

    public ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
        => new(grains.GetGrain<IGrantStore>(IdentityGrains.Grants(caller.WorkspaceId))
            .ListAsync(cancellationToken));

    public async ValueTask ConsumeOnceAsync(CallerContext caller, IReadOnlyList<Grant> grants, CancellationToken cancellationToken)
    {
        var store = grains.GetGrain<IGrantStore>(IdentityGrains.Grants(caller.WorkspaceId));
        foreach (var grant in grants)
        {
            await store.RevokeAsync(grant.AppId, grant.SemanticTypeId, GrantMode.Once, cancellationToken).ConfigureAwait(false);
        }
    }
}
