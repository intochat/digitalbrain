using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;

namespace DigitalBrain.Runtime.Orleans.Capabilities;

/// <summary>
/// Wraps an <see cref="IIdentityDirectoryPort"/> so internal directory neuron invocations run under a platform caller.
/// </summary>
internal sealed class PlatformIdentityDirectoryPort(IIdentityDirectoryPort inner) : IIdentityDirectoryPort
{
    private static readonly CapabilityCaller PlatformCaller = CapabilityCallers.Platform("identity.directory", "system", "system");

    public IIdentityDirectoryPort Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.ReadAccount(accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.OwnerOfBrain(brainId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task EnsureAccount(Account account, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await Inner.EnsureAccount(account, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.EnsureBrain(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.ProvisionOwner(owner, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.IsRegistered(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.RegisterPrincipal(principalId, displayName, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.CompleteRegistration(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<DigitalBrain.Capabilities.Identity.BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.BrainsOf(principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.KindOf(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.BrainExists(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.Membership(brainScopeId, principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Member?> EnsureMember(CapabilityCaller caller, Member member, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.EnsureMember(caller, member, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemoveMember(CapabilityCaller caller, string brainScopeId, string accountId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await Inner.RemoveMember(caller, brainScopeId, accountId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RemovePrincipalMember(CapabilityCaller caller, string brainScopeId, string principalId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await Inner.RemovePrincipalMember(caller, brainScopeId, principalId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CallDecision?> Authorize(CapabilityCaller caller, CallRequest request, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.Authorize(caller, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.ListGrants(brainScopeId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Grant?> GrantAsOwner(CapabilityCaller caller, Grant grant, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.GrantAsOwner(caller, grant, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> RevokeAsOwner(CapabilityCaller caller, string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.RevokeAsOwner(caller, appId, semanticTypeId, mode, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Wraps an <see cref="IIdentitySessionPort"/> so internal session neuron invocations run under a platform caller.
/// </summary>
internal sealed class PlatformIdentitySessionPort(IIdentitySessionPort inner) : IIdentitySessionPort
{
    private static readonly CapabilityCaller PlatformCaller = CapabilityCallers.Platform("identity.sessions", "system", "system");

    public IIdentitySessionPort Inner { get; } = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<SessionIssued> Issue(CapabilityCaller caller, SessionRequest request, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.Issue(caller, request, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task End(CapabilityCaller caller, string sessionId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            await Inner.End(caller, sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<Member?> Validate(string sessionId, CancellationToken cancellationToken)
    {
        using (RuntimeCallerContext.Stamp(PlatformCaller))
        {
            return await Inner.Validate(sessionId, cancellationToken).ConfigureAwait(false);
        }
    }

    public IdentityProviderDescriptor Describe() => Inner.Describe();
}
