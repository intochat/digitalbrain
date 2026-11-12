namespace DigitalBrain.Identity.Local.Tests.Unit;

using System.Text.Json;
using DigitalBrain;
using DigitalBrain.Brains;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Identity.Model;
using DigitalBrain.Identity.Local.Directory;
using DigitalBrain.Testing;
using DigitalBrain.Testing.Conformance.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

/// <summary>
/// Adversarial challenger test harness verifying Identity domain invariants (Milestone 4 / R12).
/// Invariants challenged:
/// 1. Non-owner rejection across pure models, simulation, and local Orleans directory.
/// 2. Account hijacking prevention with identical operationId across distinct accounts.
/// 3. Grant exhaustion (deterministic Once grant spending & single-win concurrency).
/// 4. Plaintext token leak prevention in state, signals, and serialization.
/// </summary>
public sealed class IdentityChallengerAdversarialFacts
{
    private const string TargetApp = "app-victim";
    private const string TargetSemanticType = "kit.storage";

    #region Invariant 1: Non-Owner Rejection

    [Fact]
    public void BrainDirectory_PureAggregate_RejectsNonOwner_And_AppCallers()
    {
        var owner = new Member { PrincipalId = "owner-alice", AccountId = "acc-alice", BrainId = "brain-1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var guest = new Member { PrincipalId = "guest-bob", AccountId = "acc-bob", BrainId = "brain-1", Role = MemberRole.Member, DisplayName = "Bob" };
        var intruder = new Member { PrincipalId = "intruder-eve", AccountId = "acc-eve", BrainId = "brain-1", Role = MemberRole.Member, DisplayName = "Eve" };
        var dir = new BrainDirectory { BrainId = "brain-1", AccountId = "acc-alice", Members = [owner, guest] };
        var now = DateTimeOffset.UtcNow;

        var guestCaller = new CapabilityCaller("guest-bob", "acc-bob", "brain-1");
        var appCaller = new CapabilityCaller("app:rogue", "acc-alice", "brain-1", "rogue-app");
        var spoofedOwnerAppCaller = new CapabilityCaller("owner-alice", "acc-alice", "brain-1", "malicious-app");

        // 1. EnsureMember (AddMember)
        Assert.Equal(Refusal.NotOwner, dir.AddMember(guestCaller, intruder, now).Refused);
        Assert.Throws<UnauthorizedAccessException>(() => dir.AddMember(guestCaller, intruder, now).OrThrow(guestCaller, "brain-1"));
        Assert.Equal(Refusal.NotOwner, dir.AddMember(appCaller, intruder, now).Refused);
        Assert.Equal(Refusal.NotOwner, dir.AddMember(spoofedOwnerAppCaller, intruder, now).Refused);

        // 2. RemoveMember (by account)
        Assert.Equal(Refusal.NotOwner, dir.RemoveMember(guestCaller, "acc-bob").Refused);
        Assert.Throws<UnauthorizedAccessException>(() => dir.RemoveMember(guestCaller, "acc-bob").OrThrow(guestCaller, "brain-1"));
        Assert.Equal(Refusal.NotOwner, dir.RemoveMember(appCaller, "acc-bob").Refused);
        Assert.Equal(Refusal.NotOwner, dir.RemoveMember(spoofedOwnerAppCaller, "acc-bob").Refused);

        // 3. RemovePrincipalMember (by principal)
        Assert.Equal(Refusal.NotOwner, dir.RemovePrincipal(guestCaller, "guest-bob").Refused);
        Assert.Throws<UnauthorizedAccessException>(() => dir.RemovePrincipal(guestCaller, "guest-bob").OrThrow(guestCaller, "brain-1"));
        Assert.Equal(Refusal.NotOwner, dir.RemovePrincipal(appCaller, "guest-bob").Refused);
        Assert.Equal(Refusal.NotOwner, dir.RemovePrincipal(spoofedOwnerAppCaller, "guest-bob").Refused);

        // 4. RecordGrant
        var grant = new Grant { AppId = "app-test", WorkspaceId = "brain-1", SemanticTypeId = "storage", Mode = GrantMode.Once };
        Assert.Equal(Refusal.NotOwner, dir.RecordGrant(guestCaller, grant, now).Refused);
        Assert.Equal(Refusal.NotOwner, dir.RecordGrant(appCaller, grant, now).Refused);
        Assert.Equal(Refusal.NotOwner, dir.RecordGrant(spoofedOwnerAppCaller, grant, now).Refused);

        // 5. RevokeGrant
        Assert.Equal(Refusal.NotOwner, dir.RevokeGrant(guestCaller, "app-test", "storage", GrantMode.Once).Refused);
        Assert.Equal(Refusal.NotOwner, dir.RevokeGrant(appCaller, "app-test", "storage", GrantMode.Once).Refused);

        // 6. Owner cannot be removed even by the owner themselves
        var ownerCaller = new CapabilityCaller("owner-alice", "acc-alice", "brain-1");
        var removeOwnerByAcc = dir.RemoveMember(ownerCaller, "acc-alice");
        Assert.Equal(Refusal.OwnerStays, removeOwnerByAcc.Refused);
        Assert.Throws<InvalidOperationException>(() => removeOwnerByAcc.OrThrow(ownerCaller, "brain-1"));

        var removeOwnerByPrincipal = dir.RemovePrincipal(ownerCaller, "owner-alice");
        Assert.Equal(Refusal.OwnerStays, removeOwnerByPrincipal.Refused);
        Assert.Throws<InvalidOperationException>(() => removeOwnerByPrincipal.OrThrow(ownerCaller, "brain-1"));
    }

    [Fact]
    public async Task InMemoryIdentityDirectory_RefusesNonOwnerOperations()
    {
        var dir = new InMemoryIdentityDirectory();
        var ct = CancellationToken.None;

        var ownerProvision = await dir.RegisterPrincipal("alice", "Alice", ct);
        var guestProvision = await dir.RegisterPrincipal("bob", "Bob", ct);

        var brainId = ownerProvision.Member.BrainId;
        var guest = new Member { PrincipalId = "bob", AccountId = guestProvision.Member.AccountId, BrainId = brainId, Role = MemberRole.Member, DisplayName = "Bob" };
        var intruder = new Member { PrincipalId = "eve", AccountId = "acc-eve", BrainId = brainId, Role = MemberRole.Member, DisplayName = "Eve" };

        var ownerCaller = new CapabilityCaller("alice", ownerProvision.Member.AccountId, brainId);
        var guestCaller = new CapabilityCaller("bob", guestProvision.Member.AccountId, brainId);
        var appCaller = new CapabilityCaller("app:test", ownerProvision.Member.AccountId, brainId, "test-app");

        // Owner adds guest
        await dir.EnsureMember(ownerCaller, guest, ct);

        // Guest attempts EnsureMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.EnsureMember(guestCaller, intruder, ct));

        // Guest attempts RemoveMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.RemoveMember(guestCaller, brainId, guestProvision.Member.AccountId, ct));

        // Guest attempts RemovePrincipalMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.RemovePrincipalMember(guestCaller, brainId, "bob", ct));

        // App caller attempts EnsureMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.EnsureMember(appCaller, intruder, ct));

        // App caller attempts RemoveMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.RemoveMember(appCaller, brainId, guestProvision.Member.AccountId, ct));

        // App caller attempts RemovePrincipalMember
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => dir.RemovePrincipalMember(appCaller, brainId, "bob", ct));

        // Non-owner GrantAsOwner returns null
        var grant = new Grant { AppId = "app-test", WorkspaceId = brainId, SemanticTypeId = "storage", Mode = GrantMode.Once };
        Assert.Null(await dir.GrantAsOwner(guestCaller, grant, ct));
        Assert.Null(await dir.GrantAsOwner(appCaller, grant, ct));

        // Non-owner RevokeAsOwner returns false
        Assert.False(await dir.RevokeAsOwner(guestCaller, "app-test", "storage", GrantMode.Once, ct));
        Assert.False(await dir.RevokeAsOwner(appCaller, "app-test", "storage", GrantMode.Once, ct));
    }

    [Fact]
    public async Task LocalDirectoryPort_And_DirectoryNeuron_EnforcesNonOwnerRejection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "db-challenger-no-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var os = TestOS.Local();
            os.Register<DirectoryNeuron>();
            os.Register<SessionsNeuron>();

            var port = new LocalDirectoryPort((type, key) => LocalIdentityConformance.ResolveNeuron(os, type, key));
            var ct = CancellationToken.None;

            var ownerProvision = await port.RegisterPrincipal("alice", "Alice", ct);
            var guestProvision = await port.RegisterPrincipal("bob", "Bob", ct);

            var brainId = ownerProvision.Member.BrainId;
            var guest = new Member { PrincipalId = "bob", AccountId = guestProvision.Member.AccountId, BrainId = brainId, Role = MemberRole.Member, DisplayName = "Bob" };
            var intruder = new Member { PrincipalId = "eve", AccountId = "acc-eve", BrainId = brainId, Role = MemberRole.Member, DisplayName = "Eve" };

            var ownerCaller = new CapabilityCaller("alice", ownerProvision.Member.AccountId, brainId);
            var guestCaller = new CapabilityCaller("bob", guestProvision.Member.AccountId, brainId);
            var appCaller = new CapabilityCaller("app:test", ownerProvision.Member.AccountId, brainId, "test-app");

            await port.EnsureMember(ownerCaller, guest, ct);

            // Verify non-owner callers are refused with UnauthorizedAccessException
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.EnsureMember(guestCaller, intruder, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.EnsureMember(appCaller, intruder, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.RemoveMember(guestCaller, brainId, guestProvision.Member.AccountId, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.RemoveMember(appCaller, brainId, guestProvision.Member.AccountId, ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.RemovePrincipalMember(guestCaller, brainId, "bob", ct));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => port.RemovePrincipalMember(appCaller, brainId, "bob", ct));

            // Owner removal is refused
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.RemoveMember(ownerCaller, brainId, ownerProvision.Member.AccountId, ct));
            await Assert.ThrowsAsync<InvalidOperationException>(() => port.RemovePrincipalMember(ownerCaller, brainId, "alice", ct));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    #endregion

    #region Invariant 2: Account Hijacking Challenge

    [Fact]
    public void AccountBook_IdenticalOperationId_AcrossDifferentAccounts_PreventsHijack()
    {
        var book = new AccountBook();
        var now = DateTimeOffset.UtcNow;
        const string duplicateOpId = "client-deterministic-op-101";

        var (b1, _, _) = book.EnsureAccount("acc-victim", "Victim", "p-victim", now, now);
        var (b2, _, _) = b1.EnsureAccount("acc-attacker", "Attacker", "p-attacker", now, now);

        // Victim creates brain with operationId
        var (b3, provVictim, factsVictim) = b2.CreateBrain("acc-victim", "p-victim", duplicateOpId, "Victim Brain", BrainKind.Circle, now);
        Assert.True(provVictim.NewBrain);
        Assert.NotEmpty(factsVictim);
        var victimBrainId = provVictim.Member.BrainId;

        // Attacker attempts to hijack with the EXACT same operationId
        var (b4, provAttacker, factsAttacker) = b3.CreateBrain("acc-attacker", "p-attacker", duplicateOpId, "Attacker Brain", BrainKind.Circle, now);
        Assert.True(provAttacker.NewBrain);
        Assert.NotEmpty(factsAttacker);
        var attackerBrainId = provAttacker.Member.BrainId;

        // Invariant: Attacker MUST NOT receive Victim's brain ID
        Assert.NotEqual(victimBrainId, attackerBrainId);
        Assert.Equal("acc-victim", provVictim.Member.AccountId);
        Assert.Equal("acc-attacker", provAttacker.Member.AccountId);

        // Invariant: Both operations remain idempotent within their own account scope
        var (b5, provVictimRepeat, factsVictimRepeat) = b4.CreateBrain("acc-victim", "p-victim", duplicateOpId, "Victim Brain", BrainKind.Circle, now);
        Assert.False(provVictimRepeat.NewBrain);
        Assert.Empty(factsVictimRepeat);
        Assert.Equal(victimBrainId, provVictimRepeat.Member.BrainId);

        var (_, provAttackerRepeat, factsAttackerRepeat) = b5.CreateBrain("acc-attacker", "p-attacker", duplicateOpId, "Attacker Brain", BrainKind.Circle, now);
        Assert.False(provAttackerRepeat.NewBrain);
        Assert.Empty(factsAttackerRepeat);
        Assert.Equal(attackerBrainId, provAttackerRepeat.Member.BrainId);
    }

    [Fact]
    public async Task DirectoryNeuron_ConcurrentCreateBrain_WithIdenticalOperationId_IsIsolated()
    {
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        os.Register<SessionsNeuron>();

        var port = new LocalDirectoryPort((type, key) => LocalIdentityConformance.ResolveNeuron(os, type, key));
        var ct = CancellationToken.None;

        const string sharedOpId = "contested-operation-xyz";

        // Concurrently invoke CreateBrain for two accounts with the identical operation ID
        var taskA = Task.Run(() => port.CreateBrain("acc-alpha", "principal-alpha", sharedOpId, "Alpha Brain", BrainKind.Circle, ct));
        var taskB = Task.Run(() => port.CreateBrain("acc-beta", "principal-beta", sharedOpId, "Beta Brain", BrainKind.Circle, ct));

        var results = await Task.WhenAll(taskA, taskB);
        var alphaProv = results[0];
        var betaProv = results[1];

        Assert.True(alphaProv.NewBrain);
        Assert.True(betaProv.NewBrain);
        Assert.NotEqual(alphaProv.Member.BrainId, betaProv.Member.BrainId);

        // Verify owners
        var ownerAlpha = await port.OwnerOfBrain(alphaProv.Member.BrainId, ct);
        var ownerBeta = await port.OwnerOfBrain(betaProv.Member.BrainId, ct);

        Assert.Equal("acc-alpha", ownerAlpha?.AccountId);
        Assert.Equal("acc-beta", ownerBeta?.AccountId);
    }

    #endregion

    #region Invariant 3: Grant Exhaustion Challenge

    [Fact]
    public void BrainDirectory_PureModel_OnceGrantStrictSpending()
    {
        var owner = new Member { PrincipalId = "alice", AccountId = "acc-alice", BrainId = "b1", Role = MemberRole.Owner, DisplayName = "Alice" };
        var onceGrant = new Grant { AppId = TargetApp, WorkspaceId = "b1", SemanticTypeId = TargetSemanticType, Mode = GrantMode.Once };
        var dir = new BrainDirectory { BrainId = "b1", AccountId = "acc-alice", Members = [owner], Grants = [onceGrant] };

        var caller = new CapabilityCaller($"app:{TargetApp}", "acc-alice", "b1", TargetApp);
        var request = new CallRequest { TargetNeuron = "kit/storage", Operation = "write", SemanticTypeIds = [TargetSemanticType] };

        // 1. First authorization succeeds and consumes Once grant
        var (outcome1, decision1) = dir.Authorize(caller, request);
        Assert.True(decision1.Allowed);
        Assert.True(decision1.ConsumesOnce);
        Assert.True(outcome1.Changed);
        Assert.Single(outcome1.Facts);
        var fact = Assert.IsType<GrantConsumed>(outcome1.Facts[0]);
        Assert.Equal(TargetApp, fact.AppId);
        Assert.Equal(TargetSemanticType, fact.SemanticTypeId);
        Assert.True(outcome1.Next.Grants[0].Revoked);

        // 2. Second authorization on consumed state is strictly denied
        var (outcome2, decision2) = outcome1.Next.Authorize(caller, request);
        Assert.False(decision2.Allowed);
        Assert.Equal(CallDenial.MissingGrant, decision2.Denial);
        Assert.False(outcome2.Changed);

        // 3. Third authorization also strictly denied
        var (outcome3, decision3) = outcome2.Next.Authorize(caller, request);
        Assert.False(decision3.Allowed);
    }

    [Fact]
    public async Task LocalDirectoryPort_OnceGrant_CannotBeReused()
    {
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        os.Register<SessionsNeuron>();

        var port = new LocalDirectoryPort((type, key) => LocalIdentityConformance.ResolveNeuron(os, type, key));
        var ct = CancellationToken.None;

        var ownerProv = await port.RegisterPrincipal("alice", "Alice", ct);
        var brainId = ownerProv.Member.BrainId;
        var ownerCaller = new CapabilityCaller("alice", ownerProv.Member.AccountId, brainId);
        var appCaller = new CapabilityCaller($"app:{TargetApp}", ownerProv.Member.AccountId, brainId, TargetApp);

        var grant = new Grant { AppId = TargetApp, WorkspaceId = brainId, SemanticTypeId = TargetSemanticType, Mode = GrantMode.Once };
        var recorded = await port.GrantAsOwner(ownerCaller, grant, ct);
        Assert.NotNull(recorded);

        var request = new CallRequest { TargetNeuron = "storage", Operation = "put", SemanticTypeIds = [TargetSemanticType] };

        // First call allowed
        var first = await port.Authorize(appCaller, request, ct);
        Assert.NotNull(first);
        Assert.True(first.Allowed);
        Assert.True(first.ConsumesOnce);

        // Second call denied
        var second = await port.Authorize(appCaller, request, ct);
        Assert.NotNull(second);
        Assert.False(second.Allowed);
        Assert.Equal(CallDenial.MissingGrant, second.Denial);
    }

    [Fact]
    public async Task DirectoryNeuron_ConcurrentOnceGrantConsumption_HasExactlyOneWinner()
    {
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        os.Register<SessionsNeuron>();

        var port = new LocalDirectoryPort((type, key) => LocalIdentityConformance.ResolveNeuron(os, type, key));
        var ct = CancellationToken.None;

        var ownerProv = await port.RegisterPrincipal("alice", "Alice", ct);
        var brainId = ownerProv.Member.BrainId;
        var ownerCaller = new CapabilityCaller("alice", ownerProv.Member.AccountId, brainId);
        var appCaller = new CapabilityCaller($"app:{TargetApp}", ownerProv.Member.AccountId, brainId, TargetApp);

        var grant = new Grant { AppId = TargetApp, WorkspaceId = brainId, SemanticTypeId = TargetSemanticType, Mode = GrantMode.Once };
        await port.GrantAsOwner(ownerCaller, grant, ct);

        var request = new CallRequest { TargetNeuron = "storage", Operation = "put", SemanticTypeIds = [TargetSemanticType] };

        // Launch 10 concurrent Authorize calls
        const int concurrentAttempts = 10;
        var tasks = Enumerable.Range(0, concurrentAttempts)
            .Select(_ => Task.Run(() => port.Authorize(appCaller, request, ct)))
            .ToArray();

        var decisions = await Task.WhenAll(tasks);

        var allowedCount = decisions.Count(d => d is { Allowed: true });
        var deniedCount = decisions.Count(d => d is { Allowed: false, Denial: CallDenial.MissingGrant });

        // INVARIANT: Exactly one caller must win the Once grant; all other 9 must be denied!
        Assert.Equal(1, allowedCount);
        Assert.Equal(concurrentAttempts - 1, deniedCount);
    }

    #endregion

    #region Invariant 4: Token Leak Challenge

    [Fact]
    public void SessionBook_NeverStores_NorSerializes_PlaintextSessionTokens()
    {
        var book = new SessionBook();
        const string rawSecretToken = "session-canary-secret-super-unguessable-999999999";
        var tokenHash = SessionBook.HashToken(rawSecretToken);
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddHours(2);

        var (next, session, facts) = book.Start(tokenHash, "brain-test", "alice", expires, now);

        // 1. Session record in state contains only the hash
        Assert.Equal(tokenHash, session.TokenHash);
        Assert.DoesNotContain(rawSecretToken, session.TokenHash, StringComparison.Ordinal);

        // 2. State inspection has no raw token anywhere
        foreach (var s in next.Sessions)
        {
            Assert.DoesNotContain("session-canary", s.TokenHash, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(64, s.TokenHash.Length); // SHA256 hex length
        }

        // 3. Serialized SessionBook JSON must not contain rawSecretToken
        var bookJson = JsonSerializer.Serialize(next);
        Assert.DoesNotContain("session-canary", bookJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawSecretToken, bookJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(tokenHash, bookJson, StringComparison.Ordinal);

        // 4. Emitted SessionStarted fact must not contain rawSecretToken
        Assert.Single(facts);
        var startedFact = Assert.IsType<SessionStarted>(facts[0]);
        Assert.Equal(tokenHash, startedFact.TokenHash);
        var factJson = JsonSerializer.Serialize<Signal>(startedFact);
        Assert.DoesNotContain("session-canary", factJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(rawSecretToken, factJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SessionsNeuron_And_LocalDirectoryPort_EndToEnd_PlaintextTokenNeverReachesStoreState()
    {
        await using var os = TestOS.Local();
        os.Register<DirectoryNeuron>();
        os.Register<SessionsNeuron>();

        var port = new LocalDirectoryPort((type, key) => LocalIdentityConformance.ResolveNeuron(os, type, key));
        var ct = CancellationToken.None;

        var ownerProv = await port.RegisterPrincipal("alice", "Alice", ct);
        var owner = ownerProv.Member;
        var ownerCaller = new CapabilityCaller("alice", owner.AccountId, owner.BrainId);

        // Issue session
        var issued = await port.Issue(ownerCaller, new SessionRequest { Member = owner }, ct);
        var rawSessionId = issued.SessionId;
        Assert.StartsWith("session-", rawSessionId, StringComparison.Ordinal);

        // Access the underlying SessionsNeuron inspectable state via TestOS / IBrainObserver
        var store = (ISessionStore)LocalIdentityConformance.ResolveNeuron(os, typeof(ISessionStore), ISessionStore.Key);
        var sessionId = new NeuronId(new BrainId("system"), ISessionStore.Type, "main");
        var stateElement = await ((DigitalBrain.Kernel.IBrainObserver)os).State(sessionId, ct);
        Assert.NotNull(stateElement);
        var serializedInspectableState = stateElement.Value.GetRawText();

        // INVARIANT 1: Plaintext session token must NEVER exist in inspectable state
        Assert.DoesNotContain(rawSessionId, serializedInspectableState, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session-", serializedInspectableState, StringComparison.OrdinalIgnoreCase);

        // INVARIANT 2: Hashed token must be present in inspectable state
        var expectedHash = SessionBook.HashToken(rawSessionId);
        Assert.Contains(expectedHash, serializedInspectableState, StringComparison.Ordinal);

        // INVARIANT 3: Store.Find fails when passed raw session ID (proves hash lookup is mandatory)
        var findRaw = await store.Find(rawSessionId);
        Assert.Null(findRaw);

        // INVARIANT 4: Store.Find succeeds ONLY when passed the SHA256 hash of raw session ID
        var findHashed = await store.Find(expectedHash);
        Assert.NotNull(findHashed);
        Assert.Equal(owner.PrincipalId, findHashed.PrincipalId);
        Assert.Equal(owner.BrainId, findHashed.BrainId);

        // INVARIANT 5: Validation through port succeeds (port hashes the raw session ID before calling store)
        var validatedMember = await port.Validate(rawSessionId, ct);
        Assert.NotNull(validatedMember);
        Assert.Equal(owner.PrincipalId, validatedMember.PrincipalId);

        // INVARIANT 6: Ending session by raw ID through port removes it from store
        await port.End(ownerCaller, rawSessionId, ct);
        Assert.Null(await store.Find(expectedHash));
        Assert.Null(await port.Validate(rawSessionId, ct));

        // INVARIANT 7: State inspectable after End has removed the session
        var stateAfterEnd = await ((DigitalBrain.Kernel.IBrainObserver)os).State(sessionId, ct);
        Assert.NotNull(stateAfterEnd);
        Assert.DoesNotContain(expectedHash, stateAfterEnd.Value.GetRawText(), StringComparison.Ordinal);
    }

    #endregion

    #region Additional Adversarial Stress Tests: BoundCapabilities, Simulation & Composite Grants

    [Fact]
    public async Task BoundIdentity_StrictCallerBoundary_NullAndNonOwnerRejection()
    {
        var dir = new InMemoryIdentityDirectory();
        CapabilityCaller? ambientCaller = null;
        var bound = new BoundIdentity(dir, () => ambientCaller);
        var ct = CancellationToken.None;

        var ownerProvision = await dir.RegisterPrincipal("alice", "Alice", ct);
        var guestProvision = await dir.RegisterPrincipal("bob", "Bob", ct);
        var brainId = ownerProvision.Member.BrainId;

        var guest = new Member { PrincipalId = "bob", AccountId = guestProvision.Member.AccountId, BrainId = brainId, Role = MemberRole.Member, DisplayName = "Bob" };
        var intruder = new Member { PrincipalId = "eve", AccountId = "acc-eve", BrainId = brainId, Role = MemberRole.Member, DisplayName = "Eve" };

        // --- PHASE 1: Ambient caller is null (unauthenticated call) ---
        ambientCaller = null;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.EnsureMember(guest, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RemoveMember(brainId, "bob", ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RemovePrincipalMember(brainId, "bob", ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.GrantAsOwner(new Grant { AppId = "app-1", WorkspaceId = brainId, SemanticTypeId = "type-1", Mode = GrantMode.Always }, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RevokeAsOwner("app-1", "type-1", GrantMode.Always, ct));

        var nullAuthDecision = await bound.Authorize(new CallRequest { TargetNeuron = "notes", Operation = "get", SemanticTypeIds = ["notes"] }, ct);
        Assert.NotNull(nullAuthDecision);
        Assert.False(nullAuthDecision.Allowed);
        Assert.Equal(CallDenial.NotAuthenticated, nullAuthDecision.Denial);

        // --- PHASE 2: Ambient caller is owner -> EnsureMember guest succeeds ---
        ambientCaller = new CapabilityCaller("alice", ownerProvision.Member.AccountId, brainId);
        var ensured = await bound.EnsureMember(guest, ct);
        Assert.NotNull(ensured);

        // --- PHASE 3: Ambient caller is guest (non-owner) -> all mutations fail with UnauthorizedAccessException ---
        ambientCaller = new CapabilityCaller("bob", guestProvision.Member.AccountId, brainId);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.EnsureMember(intruder, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RemoveMember(brainId, guestProvision.Member.AccountId, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RemovePrincipalMember(brainId, "bob", ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.GrantAsOwner(new Grant { AppId = "app-1", WorkspaceId = brainId, SemanticTypeId = "type-1", Mode = GrantMode.Always }, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bound.RevokeAsOwner("app-1", "type-1", GrantMode.Always, ct));
    }

    [Fact]
    public async Task InMemoryIdentityDirectory_MultiTypeGrant_OnceConsumedIndividually()
    {
        var dir = new InMemoryIdentityDirectory();
        var ct = CancellationToken.None;

        var ownerProv = await dir.RegisterPrincipal("alice", "Alice", ct);
        var brainId = ownerProv.Member.BrainId;
        var ownerCaller = new CapabilityCaller("alice", ownerProv.Member.AccountId, brainId);

        const string typeOnce = "res.once";
        const string typeAlways = "res.always";

        // Grant typeOnce as Once, and typeAlways as Always
        await dir.GrantAsOwner(ownerCaller, new Grant { AppId = TargetApp, WorkspaceId = brainId, SemanticTypeId = typeOnce, Mode = GrantMode.Once }, ct);
        await dir.GrantAsOwner(ownerCaller, new Grant { AppId = TargetApp, WorkspaceId = brainId, SemanticTypeId = typeAlways, Mode = GrantMode.Always }, ct);

        var appCaller = new CapabilityCaller($"app:{TargetApp}", ownerProv.Member.AccountId, brainId, TargetApp);

        // Call requiring BOTH types
        var combinedRequest = new CallRequest { TargetNeuron = "hub", Operation = "exec", SemanticTypeIds = [typeOnce, typeAlways] };
        var decision1 = await dir.Authorize(appCaller, combinedRequest, ct);
        Assert.NotNull(decision1);
        Assert.True(decision1.Allowed);
        Assert.True(decision1.ConsumesOnce);

        // Second call requiring BOTH types: typeOnce is consumed, so request is DENIED
        var decision2 = await dir.Authorize(appCaller, combinedRequest, ct);
        Assert.NotNull(decision2);
        Assert.False(decision2.Allowed);
        Assert.Equal(CallDenial.MissingGrant, decision2.Denial);

        // But call requiring ONLY typeAlways remains ALLOWED because Always grant was not affected!
        var alwaysRequest = new CallRequest { TargetNeuron = "hub", Operation = "exec", SemanticTypeIds = [typeAlways] };
        var decisionAlways = await dir.Authorize(appCaller, alwaysRequest, ct);
        Assert.NotNull(decisionAlways);
        Assert.True(decisionAlways.Allowed);
        Assert.False(decisionAlways.ConsumesOnce);
    }

    #endregion
}
