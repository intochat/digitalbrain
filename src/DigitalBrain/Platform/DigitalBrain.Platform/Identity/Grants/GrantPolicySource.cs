using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Identity.Authority;
using DigitalBrain.Platform.Identity.Configuration;
using Microsoft.Extensions.Options;
using Orleans;

namespace DigitalBrain.Platform.Identity.Grants;

internal interface IGrantPolicySource
{
    ValueTask<CallDecision?> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken);
}

internal sealed class GrainGrantPolicySource(IGrainFactory grains, IOptions<AuthOptions>? auth = null) : IGrantPolicySource
{
    public ValueTask<CallDecision?> AuthorizeAsync(CallRequest request, CancellationToken cancellationToken)
    {
        var caller = request.Caller;
        if (request.PublicOperation) { return ValueTask.FromResult<CallDecision?>(null); }
        if (caller.Kind == CallerKind.Platform) { return ValueTask.FromResult<CallDecision?>(null); }
        if (auth?.Value.Posture == IdentityPosture.Open && caller.PrincipalId == AccountSession.DefaultLogin
            && caller.AccountId == AccountSession.DefaultLogin && caller.Kind is CallerKind.User or CallerKind.Assistant)
        { return ValueTask.FromResult<CallDecision?>(null); }
        return new(Authority(caller).Authorize(request).WaitAsync(cancellationToken));
    }
    public async ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
        => await Authority(caller).ListGrants().WaitAsync(cancellationToken);
    private IBrainAuthority Authority(CallerContext caller)
        => grains.GetGrain<IBrainAuthority>(BrainScope.Create(caller.AccountId, caller.BrainId).Id);
}
