namespace DigitalBrain.Testing.Conformance.Identity;

using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Modules;

/// <summary>
/// In-memory reference implementation of identity capabilities (<see cref="IIdentity"/>, <see cref="IGrants"/>,
/// <see cref="ISessions"/>, <see cref="IIdentityProvider"/>) over <see cref="InMemoryIdentityDirectory"/>,
/// bound with ambient caller support.
/// </summary>
public class InMemoryIdentity : IIdentity, IGrants, ISessions, IIdentityProvider
{
    /// <summary>The default session lifetime.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(8);

    private readonly AmbientCallerScope _callers;
    private readonly InMemoryIdentityDirectory _directory;
    private readonly BoundIdentity _identity;
    private readonly BoundSessions _sessions;

    /// <summary>Creates a new instance of <see cref="InMemoryIdentity"/>.</summary>
    /// <param name="callers">Ambient caller scope.</param>
    /// <param name="time">Clock source.</param>
    public InMemoryIdentity(AmbientCallerScope? callers = null, TimeProvider? time = null)
    {
        _callers = callers ?? new AmbientCallerScope();
        _directory = new InMemoryIdentityDirectory(time);
        _identity = new BoundIdentity(_directory, () => _callers.Current);
        _sessions = new BoundSessions(_directory, () => _callers.Current, Declarations.RequirePrivileged);
    }

    /// <summary>The module declarations the privileged session members check; <see cref="ModuleDeclarations.None"/> by default.</summary>
    public ModuleDeclarations Declarations
    {
        get => _declarations;
        init
        {
            _declarations = value;
            _boundSessions = new BoundSessions(_directory, () => _callers.Current, value.RequirePrivileged);
        }
    }
    private ModuleDeclarations _declarations = ModuleDeclarations.None;
    private BoundSessions? _boundSessions;

    private BoundSessions SessionsDelegate => _boundSessions ?? _sessions;

    /// <summary>What <see cref="Describe"/> answers; a Secured <c>memory</c> implementation without an account surface by default.</summary>
    public IdentityProviderDescriptor Descriptor
    {
        get => _directory.Descriptor;
        init => _directory.Descriptor = value;
    }

    /// <summary>Diagnostic hook called before each public member runs.</summary>
    /// <param name="member">The member name.</param>
    protected virtual void OnCall(string member) { }

    /// <summary>Adds a sign-in handler.</summary>
    /// <param name="handler">The handler to add.</param>
    public void AddSignInHandler(ISignInHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _directory.AddSignInHandler(handler);
    }

    /// <summary>Simulates a host sign-in.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The provisioned member.</returns>
    public Task<Member> SignIn(string principalId, CancellationToken cancellationToken)
    {
        OnCall(nameof(SignIn));
        return _directory.SignIn(principalId, cancellationToken);
    }

    // --- IIdentity ---

    /// <inheritdoc />
    public Task<Account?> ReadAccount(string accountId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(ReadAccount));
        return _identity.ReadAccount(accountId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Account?> OwnerOfBrain(string brainId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(OwnerOfBrain));
        return _identity.OwnerOfBrain(brainId, cancellationToken);
    }

    /// <inheritdoc />
    public Task EnsureAccount(Account account, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(EnsureAccount));
        return _identity.EnsureAccount(account, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> CreateBrain(string accountId, string principalId, string operationId, string displayName, BrainKind kind, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(CreateBrain));
        return _identity.CreateBrain(accountId, principalId, operationId, displayName, kind, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> EnsureBrain(Member owner, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(EnsureBrain));
        return _identity.EnsureBrain(owner, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> ProvisionOwner(Member owner, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(ProvisionOwner));
        return _identity.ProvisionOwner(owner, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsRegistered(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(IsRegistered));
        return _identity.IsRegistered(principalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision> RegisterPrincipal(string principalId, string displayName, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RegisterPrincipal));
        return _identity.RegisterPrincipal(principalId, displayName, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainProvision?> CompleteRegistration(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(CompleteRegistration));
        return _identity.CompleteRegistration(principalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrainEntry>> BrainsOf(string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(BrainsOf));
        return _identity.BrainsOf(principalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<BrainKind> KindOf(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(KindOf));
        return _identity.KindOf(brainScopeId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> BrainExists(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(BrainExists));
        return _identity.BrainExists(brainScopeId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Member?> Membership(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Membership));
        return _identity.Membership(brainScopeId, principalId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Member?> EnsureMember(Member member, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(EnsureMember));
        return _identity.EnsureMember(member, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemoveMember(string brainScopeId, string accountId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RemoveMember));
        return _identity.RemoveMember(brainScopeId, accountId, cancellationToken);
    }

    /// <inheritdoc />
    public Task RemovePrincipalMember(string brainScopeId, string principalId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RemovePrincipalMember));
        return _identity.RemovePrincipalMember(brainScopeId, principalId, cancellationToken);
    }

    // --- IGrants ---

    /// <inheritdoc />
    public Task<CallDecision?> Authorize(CallRequest request, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Authorize));
        return _identity.Authorize(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Grant>> ListGrants(string brainScopeId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(ListGrants));
        return _identity.ListGrants(brainScopeId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Grant> GrantAsOwner(Grant grant, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(GrantAsOwner));
        return _identity.GrantAsOwner(grant, cancellationToken);
    }

    /// <inheritdoc />
    public Task RevokeAsOwner(string appId, string semanticTypeId, GrantMode mode, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(RevokeAsOwner));
        return _identity.RevokeAsOwner(appId, semanticTypeId, mode, cancellationToken);
    }

    // --- ISessions ---

    /// <inheritdoc />
    public Task<SessionIssued> Issue(SessionRequest request, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Issue));
        return SessionsDelegate.Issue(request, cancellationToken);
    }

    /// <inheritdoc />
    public Task End(string sessionId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(End));
        return SessionsDelegate.End(sessionId, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Member?> Validate(string sessionId, CancellationToken cancellationToken = default)
    {
        OnCall(nameof(Validate));
        return SessionsDelegate.Validate(sessionId, cancellationToken);
    }

    // --- IIdentityProvider ---

    /// <inheritdoc />
    public IdentityProviderDescriptor Describe()
    {
        OnCall(nameof(Describe));
        return SessionsDelegate.Describe();
    }
}
