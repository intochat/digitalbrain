using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using Xunit;

namespace DigitalBrain.Testing.Conformance.Identity;

/// <summary>A connected identity@1 implementation under test.</summary>
/// <param name="Identity">The accounts, brains, principals and membership half.</param>
/// <param name="Grants">The grants half (same identity@1 connection).</param>
/// <param name="Callers">Switches the runtime caller the implementation sees.</param>
/// <param name="SignIn">
/// Optional: signs <c>principalId</c> in the way the host does (registering it if needed) and returns the member the
/// <see cref="ISignInHandler"/>s received. Null skips the sign-in fact.
/// </param>
public sealed record IdentitySubject(IIdentity Identity, IGrants Grants, CallerScope Callers, Func<string, CancellationToken, Task<Member>>? SignIn = null)
{
    /// <summary>The sessions part of the same identity@1 connection; null skips the session facts.</summary>
    public ISessions? Sessions { get; init; }

    /// <summary>The description part of the same identity@1 connection; null skips the description facts.</summary>
    public IIdentityProvider? Description { get; init; }

    /// <summary>
    /// A module id the implementation grants identity@1 <c>Issue</c>, <c>End</c> and <c>Validate</c> (its manifest
    /// <c>Privileged</c> list). Required by the session facts; every other module id must be refused.
    /// </summary>
    public string? PrivilegedModule { get; init; }
}

/// <summary>
/// The identity@1 conformance kit (CAP-01). A module providing identity subclasses it in its <c>tests/</c> project as a sealed
/// class and implements <see cref="Connect"/>; every inherited fact runs against that subject. Each fact provisions its own
/// principals with unique ids through <see cref="IIdentity.RegisterPrincipal"/>. Grants are scoped to a brain:
/// <see cref="Grant.WorkspaceId"/> is the brain id the app caller runs in. Facts are additive: Phase 9 identity@1 members
/// (D-08) arrive as new facts, not changes to these.
/// </summary>
/// <typeparam name="TIdentity">The implementation under test (names the subclass's subject; not otherwise used).</typeparam>
public abstract class IdentityConformance<TIdentity>
{
    /// <summary>Every fact's timeout in milliseconds.</summary>
    protected const int Timeout = 30_000;

    private const string SemanticType = "kit.note";

    /// <summary>Connects to the implementation. Called once per fact.</summary>
    protected abstract Task<IdentitySubject> Connect(CancellationToken ct);

    // ----- accounts and brains -----

    /// <summary>EnsureAccount twice keeps one account with the same id and owner.</summary>
    [Fact(Timeout = Timeout)]
    public async Task EnsureAccountIsIdempotent()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var id = Unique("account");
        var account = new Account { AccountId = id, Name = "Kit account", OwnerPrincipalId = Unique("p") };
        await s.Identity.EnsureAccount(account, ct).ConfigureAwait(false);
        await s.Identity.EnsureAccount(account with { Name = "Kit account again" }, ct).ConfigureAwait(false);
        var read = await s.Identity.ReadAccount(id, ct).ConfigureAwait(false);
        Assert.NotNull(read);
        Assert.Equal(id, read.AccountId);
        Assert.Equal(account.OwnerPrincipalId, read.OwnerPrincipalId);
        Assert.Null(await s.Identity.ReadAccount(Unique("missing"), ct).ConfigureAwait(false));
    }

    /// <summary>CreateBrain makes the creator the owner; OwnerOfBrain is the creator's account; the brain exists with its kind.</summary>
    [Fact(Timeout = Timeout)]
    public async Task CreateBrainThenOwnerOfBrainIsCreator()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var (accountId, principalId) = await Account(s, ct).ConfigureAwait(false);
        var created = await s.Identity.CreateBrain(accountId, principalId, Unique("op"), "Kit circle", BrainKind.Circle, ct).ConfigureAwait(false);
        Assert.True(created.NewBrain, "a first CreateBrain reported an existing brain");
        Assert.Equal(principalId, created.Member.PrincipalId);
        Assert.Equal(MemberRole.Owner, created.Member.Role);
        var brainId = created.Member.BrainId;
        Assert.Equal(accountId, (await s.Identity.OwnerOfBrain(brainId, ct).ConfigureAwait(false))?.AccountId);
        Assert.True(await s.Identity.BrainExists(brainId, ct).ConfigureAwait(false), $"{brainId} does not exist");
        Assert.Equal(BrainKind.Circle, await s.Identity.KindOf(brainId, ct).ConfigureAwait(false));
    }

    /// <summary>CreateBrain with the same operation id twice returns the same brain, the second time not new.</summary>
    [Fact(Timeout = Timeout)]
    public async Task CreateBrainIsIdempotentPerOperation()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var (accountId, principalId) = await Account(s, ct).ConfigureAwait(false);
        var op = Unique("op");
        var first = await s.Identity.CreateBrain(accountId, principalId, op, "Kit circle", BrainKind.Circle, ct).ConfigureAwait(false);
        var second = await s.Identity.CreateBrain(accountId, principalId, op, "Kit circle", BrainKind.Circle, ct).ConfigureAwait(false);
        Assert.Equal(first.Member.BrainId, second.Member.BrainId);
        Assert.False(second.NewBrain, $"{op}: a repeated CreateBrain reported a new brain");
    }

    /// <summary>BrainsOf the creator lists the created brain with its kind.</summary>
    [Fact(Timeout = Timeout)]
    public async Task BrainsOfListsCreatedBrain()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var (accountId, principalId) = await Account(s, ct).ConfigureAwait(false);
        var created = await s.Identity.CreateBrain(accountId, principalId, Unique("op"), "Kit circle", BrainKind.Circle, ct).ConfigureAwait(false);
        var brains = await ObserveBrain(s.Identity, principalId, created.Member.BrainId, ct).ConfigureAwait(false);
        Assert.Contains(new BrainEntry(created.Member.BrainId, BrainKind.Circle), brains);
    }

    /// <summary>An unknown brain is Personal and does not exist.</summary>
    [Fact(Timeout = Timeout)]
    public async Task KindOfUnknownIsPersonal()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var unknown = Unique("brain");
        Assert.Equal(BrainKind.Personal, await s.Identity.KindOf(unknown, ct).ConfigureAwait(false));
        Assert.False(await s.Identity.BrainExists(unknown, ct).ConfigureAwait(false), $"{unknown} exists");
    }

    /// <summary>ProvisionOwner then EnsureBrain for the same owner returns the same brain, not new the second time.</summary>
    [Fact(Timeout = Timeout)]
    public async Task ProvisionOwnerThenEnsureBrainIsIdempotent()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var principalId = Unique("p");
        var owner = new Member { PrincipalId = principalId, AccountId = Unique("account"), BrainId = Unique("brain"), Role = MemberRole.Owner, DisplayName = "Kit owner" };
        var first = await s.Identity.ProvisionOwner(owner, ct).ConfigureAwait(false);
        var second = await s.Identity.EnsureBrain(owner, ct).ConfigureAwait(false);
        Assert.Equal(first.Member.BrainId, second.Member.BrainId);
        Assert.False(second.NewBrain, $"{principalId}: EnsureBrain after ProvisionOwner reported a new brain");
        Assert.Equal(MemberRole.Owner, (await s.Identity.Membership(first.Member.BrainId, principalId, ct).ConfigureAwait(false))?.Role);
    }

    // ----- principals -----

    /// <summary>RegisterPrincipal registers once; again returns the same brain, not new; CompleteRegistration finds it, and null for unknown.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RegisterPrincipalIsIdempotent()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var principalId = Unique("p");
        Assert.False(await s.Identity.IsRegistered(principalId, ct).ConfigureAwait(false), $"{principalId} registered before registering");
        var first = await s.Identity.RegisterPrincipal(principalId, "Kit person", ct).ConfigureAwait(false);
        Assert.True(await s.Identity.IsRegistered(principalId, ct).ConfigureAwait(false), $"{principalId} not registered");
        Assert.Equal(MemberRole.Owner, first.Member.Role);
        var second = await s.Identity.RegisterPrincipal(principalId, "Kit person", ct).ConfigureAwait(false);
        Assert.Equal(first.Member.BrainId, second.Member.BrainId);
        Assert.False(second.NewBrain, $"{principalId}: a repeated registration reported a new brain");
        Assert.Equal(first.Member.BrainId, (await s.Identity.CompleteRegistration(principalId, ct).ConfigureAwait(false))?.Member.BrainId);
        Assert.Null(await s.Identity.CompleteRegistration(Unique("missing"), ct).ConfigureAwait(false));
    }

    /// <summary>Sign-in hands the handlers the principal's owner membership in its own brain (skips without a sign-in hook).</summary>
    [Fact(Timeout = Timeout)]
    public async Task SignInYieldsTheOwnerMember()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        Assert.SkipWhen(s.SignIn is null, "the subject exposes no sign-in hook");
        var principalId = Unique("p");
        var member = await s.SignIn!(principalId, ct).ConfigureAwait(false);
        Assert.Equal(principalId, member.PrincipalId);
        Assert.False(string.IsNullOrWhiteSpace(member.AccountId), $"{principalId}: the signed-in member has no account");
        Assert.False(string.IsNullOrWhiteSpace(member.BrainId), $"{principalId}: the signed-in member has no brain");
        Assert.Equal(member.BrainId, (await s.Identity.Membership(member.BrainId, principalId, ct).ConfigureAwait(false))?.BrainId);
    }

    // ----- membership -----

    /// <summary>EnsureMember by the owner persists: Membership returns it, BrainsOf lists the brain, a repeat is idempotent.</summary>
    [Fact(Timeout = Timeout)]
    public async Task EnsureMemberThenMembershipContainsIt()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var guest = Guest(owner);
        using (s.Callers.As(Caller(owner)))
        {
            await s.Identity.EnsureMember(guest, ct).ConfigureAwait(false);
            await s.Identity.EnsureMember(guest, ct).ConfigureAwait(false);
            var read = await s.Identity.Membership(owner.BrainId, guest.PrincipalId, ct).ConfigureAwait(false);
            Assert.NotNull(read);
            Assert.Equal(MemberRole.Member, read.Role);
            Assert.Equal(guest.AccountId, read.AccountId);
            Assert.Contains(await s.Identity.BrainsOf(guest.PrincipalId, ct).ConfigureAwait(false), entry => entry.Id == owner.BrainId);
        }
    }

    /// <summary>RemovePrincipalMember removes a member.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RemovePrincipalMemberRemovesIt()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var guest = Guest(owner);
        using (s.Callers.As(Caller(owner)))
        {
            await s.Identity.EnsureMember(guest, ct).ConfigureAwait(false);
            await s.Identity.RemovePrincipalMember(owner.BrainId, guest.PrincipalId, ct).ConfigureAwait(false);
            Assert.Null(await s.Identity.Membership(owner.BrainId, guest.PrincipalId, ct).ConfigureAwait(false));
        }
    }

    /// <summary>Removing the owner (by principal or account) either refuses or is a no-op; the owner stays.</summary>
    [Fact(Timeout = Timeout)]
    public async Task OwnerCannotBeRemoved()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        using (s.Callers.As(Caller(owner)))
        {
            await Refused(() => s.Identity.RemovePrincipalMember(owner.BrainId, owner.PrincipalId, ct)).ConfigureAwait(false);
            await Refused(() => s.Identity.RemoveMember(owner.BrainId, owner.AccountId, ct)).ConfigureAwait(false);
            Assert.Equal(MemberRole.Owner, (await s.Identity.Membership(owner.BrainId, owner.PrincipalId, ct).ConfigureAwait(false))?.Role);
            Assert.Equal(owner.AccountId, (await s.Identity.OwnerOfBrain(owner.BrainId, ct).ConfigureAwait(false))?.AccountId);
        }
    }

    /// <summary>Removing a principal that is not a member is a no-op.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RemovePrincipalMemberUnknownIsNoOp()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        using (s.Callers.As(Caller(owner)))
        {
            await s.Identity.RemovePrincipalMember(owner.BrainId, Unique("nobody"), ct).ConfigureAwait(false);
            Assert.NotNull(await s.Identity.Membership(owner.BrainId, owner.PrincipalId, ct).ConfigureAwait(false));
        }
    }

    // ----- grants -----

    /// <summary>A member who is not the owner granting throws <see cref="UnauthorizedAccessException"/> and grants nothing (T-05-05-03).</summary>
    [Fact(Timeout = Timeout)]
    public async Task GrantAsOwnerByNonOwnerThrowsUnauthorized()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var guest = Guest(owner);
        var appId = Unique("app");
        using (s.Callers.As(Caller(owner)))
        {
            await s.Identity.EnsureMember(guest, ct).ConfigureAwait(false);
        }

        using (s.Callers.As(Caller(guest)))
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => s.Grants.GrantAsOwner(NewGrant(appId, owner.BrainId, GrantMode.Always), ct)).ConfigureAwait(false);
        }

        using (s.Callers.As(AppCaller(owner, appId)))
        {
            Assert.False(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is allowed after a refused grant");
        }
    }

    /// <summary>Without a grant an app is denied with MissingGrant; a user caller is not denied.</summary>
    [Fact(Timeout = Timeout)]
    public async Task AuthorizeWithoutGrantDenies()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var appId = Unique("app");
        using (s.Callers.As(AppCaller(owner, appId)))
        {
            var decision = await s.Grants.Authorize(Request(), ct).ConfigureAwait(false);
            Assert.NotNull(decision);
            Assert.False(decision.Allowed, $"{appId} is allowed without a grant");
            Assert.Equal(CallDenial.MissingGrant, decision.Denial);
        }

        using (s.Callers.As(Caller(owner)))
        {
            Assert.True(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), "the owner is denied");
        }
    }

    /// <summary>An owner's Always grant allows the app and is listed for the brain, covering the app and type.</summary>
    [Fact(Timeout = Timeout)]
    public async Task GrantThenAuthorizeAllows()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var appId = Unique("app");
        using (s.Callers.As(Caller(owner)))
        {
            var granted = await s.Grants.GrantAsOwner(NewGrant(appId, owner.BrainId, GrantMode.Always), ct).ConfigureAwait(false);
            Assert.True(granted.Covers(appId, SemanticType, owner.BrainId, null), $"the returned grant for {appId} does not cover it");
            var listed = await s.Grants.ListGrants(owner.BrainId, ct).ConfigureAwait(false);
            Assert.Contains(listed, grant => grant.Covers(appId, SemanticType, owner.BrainId, null));
        }

        using (s.Callers.As(AppCaller(owner, appId)))
        {
            Assert.True(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is denied after a grant");
        }
    }

    /// <summary>After RevokeAsOwner the app is denied and no listed grant covers it.</summary>
    [Fact(Timeout = Timeout)]
    public async Task RevokeThenAuthorizeDenies()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var appId = Unique("app");
        using (s.Callers.As(Caller(owner)))
        {
            await s.Grants.GrantAsOwner(NewGrant(appId, owner.BrainId, GrantMode.Always), ct).ConfigureAwait(false);
            await s.Grants.RevokeAsOwner(appId, SemanticType, GrantMode.Always, ct).ConfigureAwait(false);
            var listed = await s.Grants.ListGrants(owner.BrainId, ct).ConfigureAwait(false);
            Assert.DoesNotContain(listed, grant => grant.Covers(appId, SemanticType, owner.BrainId, null));
        }

        using (s.Callers.As(AppCaller(owner, appId)))
        {
            Assert.False(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is allowed after revoke");
        }
    }

    /// <summary>A ThisChat grant allows the app in its conversation only (<see cref="Grant.Covers"/> semantics).</summary>
    [Fact(Timeout = Timeout)]
    public async Task ThisChatGrantCoversOnlyItsConversation()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        var appId = Unique("app");
        var chat = Unique("chat");
        using (s.Callers.As(Caller(owner)))
        {
            await s.Grants.GrantAsOwner(NewGrant(appId, owner.BrainId, GrantMode.ThisChat) with { ConversationId = chat }, ct).ConfigureAwait(false);
        }

        using (s.Callers.As(AppCaller(owner, appId, chat)))
        {
            Assert.True(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is denied in {chat}");
        }

        using (s.Callers.As(AppCaller(owner, appId, Unique("other-chat"))))
        {
            Assert.False(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is allowed outside {chat}");
        }

        using (s.Callers.As(AppCaller(owner, appId)))
        {
            Assert.False(Allowed(await s.Grants.Authorize(Request(), ct).ConfigureAwait(false)), $"{appId} is allowed with no conversation");
        }
    }

    // ----- provisioning under concurrency (09-03, identity row 3) -----

    /// <summary>Concurrent ProvisionOwner and EnsureBrain calls for one owner yield one account, one brain and one owner member.</summary>
    [Fact(Timeout = Timeout)]
    public async Task EnsureBrainIsIdempotentUnderConcurrentCalls()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var principalId = Unique("p");
        var owner = new Member { PrincipalId = principalId, AccountId = Unique("account"), BrainId = Unique("brain"), Role = MemberRole.Owner, DisplayName = "Kit owner" };
        var account = new Account { AccountId = owner.AccountId, Name = "Kit account", OwnerPrincipalId = principalId };
        var accounts = Enumerable.Range(0, 8).Select(_ => Task.Run(() => s.Identity.EnsureAccount(account, ct), ct));
        var provisions = await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(
            () => i % 2 == 0 ? s.Identity.ProvisionOwner(owner, ct) : s.Identity.EnsureBrain(owner, ct), ct))).ConfigureAwait(false);
        await Task.WhenAll(accounts).ConfigureAwait(false);
        Assert.Single(provisions, provision => provision.NewBrain);
        Assert.All(provisions, provision => Assert.Equal(owner.BrainId, provision.Member.BrainId));
        Assert.Equal(owner.AccountId, (await s.Identity.ReadAccount(owner.AccountId, ct).ConfigureAwait(false))?.AccountId);
        Assert.Equal(owner.AccountId, (await s.Identity.OwnerOfBrain(owner.BrainId, ct).ConfigureAwait(false))?.AccountId);
        Assert.Equal(MemberRole.Owner, (await s.Identity.Membership(owner.BrainId, principalId, ct).ConfigureAwait(false))?.Role);
        Assert.Single(await ObserveBrain(s.Identity, principalId, owner.BrainId, ct).ConfigureAwait(false), entry => entry.Id == owner.BrainId);
    }

    // ----- sessions (09-03, identity rows 1, 2 and 4) -----

    /// <summary>A granted module issues a session with extra claims and a lifetime; Validate returns the member until it expires.</summary>
    [Fact(Timeout = Timeout)]
    public async Task IssueThenValidateReturnsTheMember()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var sessions = RequireSessions(s);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        using (s.Callers.As(Privileged(s, owner)))
        {
            var before = DateTimeOffset.UtcNow;
            var issued = await sessions.Issue(new SessionRequest
            {
                Member = owner,
                Claims = new Dictionary<string, string> { ["sid"] = Unique("sid"), ["auth_time"] = "1761350400", ["amr"] = "pwd" },
                Lifetime = TimeSpan.FromMinutes(5),
            }, ct).ConfigureAwait(false);
            Assert.False(string.IsNullOrWhiteSpace(issued.SessionId), "the issued session has no id");
            Assert.InRange(issued.ExpiresAt, before.AddMinutes(4), DateTimeOffset.UtcNow.AddMinutes(6));
            var validated = await sessions.Validate(issued.SessionId, ct).ConfigureAwait(false);
            Assert.NotNull(validated);
            Assert.Equal(owner.PrincipalId, validated.PrincipalId);
            Assert.Equal(owner.AccountId, validated.AccountId);
            Assert.Equal(owner.BrainId, validated.BrainId);
            Assert.Equal(owner.Role, validated.Role);
            Assert.Null(await sessions.Validate(Unique("unknown-session"), ct).ConfigureAwait(false));
        }
    }

    /// <summary>Extra claims never replace the identity-owned principal, account, brain or role claims: Issue rejects them (T-09-07).</summary>
    [Fact(Timeout = Timeout)]
    public async Task IssueRejectsIdentityOwnedClaims()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var sessions = RequireSessions(s);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        using (s.Callers.As(Privileged(s, owner)))
        {
            foreach (var claim in new[] { "principal", "account", "brain", "role", "Principal", " role " })
            {
                var request = new SessionRequest { Member = owner, Claims = new Dictionary<string, string> { [claim] = "kit-override" } };
                await Assert.ThrowsAnyAsync<ArgumentException>(() => sessions.Issue(request, ct)).ConfigureAwait(false);
            }

            await Assert.ThrowsAnyAsync<ArgumentException>(() => sessions.Issue(new SessionRequest { Member = owner, Lifetime = TimeSpan.Zero }, ct)).ConfigureAwait(false);
        }
    }

    /// <summary>After End, Validate of that session is null; ending again is a no-op.</summary>
    [Fact(Timeout = Timeout)]
    public async Task EndThenValidateFails()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var sessions = RequireSessions(s);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        using (s.Callers.As(Privileged(s, owner)))
        {
            var issued = await sessions.Issue(new SessionRequest { Member = owner }, ct).ConfigureAwait(false);
            var other = await sessions.Issue(new SessionRequest { Member = owner }, ct).ConfigureAwait(false);
            await sessions.End(issued.SessionId, ct).ConfigureAwait(false);
            Assert.Null(await sessions.Validate(issued.SessionId, ct).ConfigureAwait(false));
            Assert.NotNull(await sessions.Validate(other.SessionId, ct).ConfigureAwait(false));
            await sessions.End(issued.SessionId, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Issue, End and Validate from a module without the manifest grant, or from a user caller, are refused with
    /// <see cref="UnauthorizedAccessException"/> (fail closed, T-09-08); the granted module succeeds.
    /// </summary>
    [Fact(Timeout = Timeout)]
    public async Task IssueFromUnprivilegedCallerIsDenied()
    {
        var (s, ct) = await Open().ConfigureAwait(false);
        var sessions = RequireSessions(s);
        var owner = await Owner(s, ct).ConfigureAwait(false);
        SessionIssued issued;
        using (s.Callers.As(Privileged(s, owner)))
        {
            issued = await sessions.Issue(new SessionRequest { Member = owner }, ct).ConfigureAwait(false);
        }

        foreach (var caller in new[] { AppCaller(owner, Unique("ungranted")), Caller(owner) })
        {
            using (s.Callers.As(caller))
            {
                await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => sessions.Issue(new SessionRequest { Member = owner }, ct)).ConfigureAwait(false);
                await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => sessions.Validate(issued.SessionId, ct)).ConfigureAwait(false);
                await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => sessions.End(issued.SessionId, ct)).ConfigureAwait(false);
            }
        }

        using (s.Callers.As(Privileged(s, owner)))
        {
            Assert.NotNull(await sessions.Validate(issued.SessionId, ct).ConfigureAwait(false));
        }
    }

    // ----- description (09-03, identity rows 6 and 9) -----

    /// <summary>Describe names the implementation and reports a defined posture (Open or Secured).</summary>
    [Fact(Timeout = Timeout)]
    public async Task DescribeReportsPosture()
    {
        var (s, _) = await Open().ConfigureAwait(false);
        Assert.SkipWhen(s.Description is null, "the subject exposes no identity@1 description");
        var descriptor = s.Description!.Describe();
        Assert.NotNull(descriptor);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.ProviderId), "the descriptor has no id");
        Assert.True(Enum.IsDefined(descriptor.Posture), $"posture {(int)descriptor.Posture} is not Open or Secured");
        Assert.Equal(descriptor, s.Description.Describe());
    }

    /// <summary>The account surface is null (no Account tab) or a well-formed neuron id.</summary>
    [Fact(Timeout = Timeout)]
    public async Task DescribeAccountSurfaceIsNullOrResolvable()
    {
        var (s, _) = await Open().ConfigureAwait(false);
        Assert.SkipWhen(s.Description is null, "the subject exposes no identity@1 description");
        if (s.Description!.Describe().AccountSurface is not { } surface) { return; }
        Assert.NotNull(surface.Brain.Value);
        Assert.False(string.IsNullOrWhiteSpace(surface.Type), "the account surface has no neuron type");
        Assert.False(string.IsNullOrWhiteSpace(surface.Key), "the account surface has no key");
        Assert.True(NeuronId.TryParse(surface.ToString(), out var parsed) && parsed == surface, $"{surface} does not round-trip");
    }

    // ----- helpers -----

    // Owner membership is authoritative before its principal index announcement arrives. Observe only that index;
    // provisioning uniqueness, account ownership and membership above still have to hold immediately.
    private static async Task<IReadOnlyList<BrainEntry>> ObserveBrain(IIdentity identity, string principalId, string brainId, CancellationToken ct)
    {
        using var observation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        observation.CancelAfter(Timeout);
        while (true)
        {
            var brains = await identity.BrainsOf(principalId, observation.Token).WaitAsync(observation.Token).ConfigureAwait(false);
            if (brains.Any(entry => entry.Id == brainId)) { return brains; }
            await Task.Delay(20, observation.Token).ConfigureAwait(false);
        }
    }

    private async Task<(IdentitySubject Subject, CancellationToken Ct)> Open()
    {
        var ct = TestContext.Current.CancellationToken;
        var subject = await Connect(ct).ConfigureAwait(false);
        Assert.NotNull(subject);
        return (subject, ct);
    }

    private static ISessions RequireSessions(IdentitySubject s)
    {
        Assert.SkipWhen(s.Sessions is null, "the subject exposes no identity@1 sessions");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(s.PrivilegedModule), "the subject names no privileged module");
        return s.Sessions!;
    }

    private static CapabilityCaller Privileged(IdentitySubject s, Member owner) => AppCaller(owner, s.PrivilegedModule!);

    private static string Unique(string prefix) => $"kit-{prefix}-{Guid.NewGuid():N}";

    private static async Task<(string AccountId, string PrincipalId)> Account(IdentitySubject s, CancellationToken ct)
    {
        var owner = await Owner(s, ct).ConfigureAwait(false);
        return (owner.AccountId, owner.PrincipalId);
    }

    private static async Task<Member> Owner(IdentitySubject s, CancellationToken ct)
    {
        var provision = await s.Identity.RegisterPrincipal(Unique("p"), "Kit owner", ct).ConfigureAwait(false);
        Assert.Equal(MemberRole.Owner, provision.Member.Role);
        return provision.Member;
    }

    private static Member Guest(Member owner) => new()
    {
        PrincipalId = Unique("guest"),
        AccountId = Unique("guest-account"),
        BrainId = owner.BrainId,
        Role = MemberRole.Member,
        DisplayName = "Kit guest",
    };

    private static CapabilityCaller Caller(Member member) => new(member.PrincipalId, member.AccountId, member.BrainId);

    private static CapabilityCaller AppCaller(Member owner, string appId, string? conversationId = null) =>
        new($"app:{appId}", owner.AccountId, owner.BrainId, appId, conversationId);

    private static Grant NewGrant(string appId, string brainId, GrantMode mode) =>
        new() { AppId = appId, SemanticTypeId = SemanticType, Mode = mode, WorkspaceId = brainId };

    private static CallRequest Request() =>
        new() { TargetNeuron = "kit/notes", Operation = "read", SemanticTypeIds = [SemanticType] };

    // A null decision means grants have no objection.
    private static bool Allowed(CallDecision? decision) => decision is null || decision.Allowed;

    private static async Task Refused(Func<Task> remove)
    {
        try
        {
            await remove().ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or UnauthorizedAccessException)
        {
        }
    }
}
