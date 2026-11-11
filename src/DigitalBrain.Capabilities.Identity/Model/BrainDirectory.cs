namespace DigitalBrain.Capabilities.Identity.Model;

using System.Collections.Immutable;
using DigitalBrain;

/// <summary>Pure domain aggregate governing membership and grants within a single brain.</summary>
public sealed record BrainDirectory
{
    /// <summary>The brain identifier.</summary>
    public required string BrainId { get; init; }

    /// <summary>The owning account identifier.</summary>
    public string AccountId { get; init; } = "";

    /// <summary>The brain kind.</summary>
    public BrainKind Kind { get; init; } = BrainKind.Personal;

    /// <summary>Members of this brain.</summary>
    public ImmutableArray<Member> Members { get; init; } = [];

    /// <summary>Grants recorded in this brain.</summary>
    public ImmutableArray<Grant> Grants { get; init; } = [];

    /// <summary>The owner member of the brain, if one exists.</summary>
    public Member? Owner => Members.FirstOrDefault(m => m.Role == MemberRole.Owner);

    /// <summary>Gets a member by principal identifier.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns>The member, or null if not a member.</returns>
    public Member? MemberOf(string principalId)
        => Members.FirstOrDefault(m => m.PrincipalId == principalId);

    /// <summary>Checks whether the given caller owns this brain.</summary>
    /// <param name="caller">The caller.</param>
    /// <returns>True if caller is the brain owner without an app context.</returns>
    public bool OwnedBy(CapabilityCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (!string.IsNullOrWhiteSpace(caller.AppId))
        {
            return false;
        }

        var owner = Owner;
        return owner is not null && owner.PrincipalId == caller.PrincipalId;
    }

    /// <summary>Adds a member to the brain.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="member">The member to add.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The transition outcome.</returns>
    public Outcome<BrainDirectory> AddMember(CapabilityCaller by, Member member, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(member);

        if (!OwnedBy(by))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.NotOwner);
        }

        if (MemberOf(member.PrincipalId) is { } existing)
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.None);
        }

        if (member.Role == MemberRole.Owner)
        {
            throw new InvalidOperationException($"Brain {BrainId} already has an owner.");
        }

        var added = member with { JoinedAt = member.JoinedAt == default ? now : member.JoinedAt };
        var next = this with { Members = Members.Add(added) };
        return new Outcome<BrainDirectory>(next, [new MemberAdded(added)], Refusal.None);
    }

    /// <summary>Removes all members associated with an account from the brain.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The transition outcome.</returns>
    public Outcome<BrainDirectory> RemoveMember(CapabilityCaller by, string accountId)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        if (!OwnedBy(by))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.NotOwner);
        }

        if (Members.Any(m => m.AccountId == accountId && m.Role == MemberRole.Owner))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.OwnerStays);
        }

        var toRemove = Members.Where(m => m.AccountId == accountId).ToImmutableArray();
        if (toRemove.IsEmpty)
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.None);
        }

        var remaining = Members.Where(m => m.AccountId != accountId).ToImmutableArray();
        var facts = toRemove.Select(m => (Signal)new MemberRemoved(BrainId, m.PrincipalId, m.AccountId)).ToImmutableArray();
        return new Outcome<BrainDirectory>(this with { Members = remaining }, facts, Refusal.None);
    }

    /// <summary>Removes a specific principal from the brain.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns>The transition outcome.</returns>
    public Outcome<BrainDirectory> RemovePrincipal(CapabilityCaller by, string principalId)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);

        if (!OwnedBy(by))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.NotOwner);
        }

        var target = MemberOf(principalId);
        if (target is null)
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.None);
        }

        if (target.Role == MemberRole.Owner)
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.OwnerStays);
        }

        var remaining = Members.Where(m => m.PrincipalId != principalId).ToImmutableArray();
        var facts = ImmutableArray.Create<Signal>(new MemberRemoved(BrainId, target.PrincipalId, target.AccountId));
        return new Outcome<BrainDirectory>(this with { Members = remaining }, facts, Refusal.None);
    }

    /// <summary>Records or updates a capability grant.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="grant">The grant to record.</param>
    /// <param name="grantedAt">The timestamp when granted.</param>
    /// <returns>The transition outcome.</returns>
    public Outcome<BrainDirectory> RecordGrant(CapabilityCaller by, Grant grant, DateTimeOffset grantedAt)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentNullException.ThrowIfNull(grant);

        var brainId = string.IsNullOrWhiteSpace(grant.WorkspaceId) ? BrainId : grant.WorkspaceId;
        if (!OwnedBy(by))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.NotOwner);
        }

        var stored = grant with { WorkspaceId = brainId, GrantedAt = grantedAt, Revoked = false };
        var remainingGrants = Grants.Where(existing => !(existing.AppId == stored.AppId
            && existing.SemanticTypeId == stored.SemanticTypeId
            && existing.Mode == stored.Mode
            && existing.WorkspaceId == stored.WorkspaceId
            && existing.ConversationId == stored.ConversationId)).ToImmutableArray();

        var next = this with { Grants = remainingGrants.Add(stored) };
        return new Outcome<BrainDirectory>(next, [new GrantRecorded(stored)], Refusal.None);
    }

    /// <summary>Revokes grants matching criteria.</summary>
    /// <param name="by">The calling capability caller.</param>
    /// <param name="appId">The application identifier.</param>
    /// <param name="semanticTypeId">The semantic type identifier.</param>
    /// <param name="mode">The grant mode.</param>
    /// <returns>The transition outcome.</returns>
    public Outcome<BrainDirectory> RevokeGrant(CapabilityCaller by, string appId, string semanticTypeId, GrantMode mode)
    {
        ArgumentNullException.ThrowIfNull(by);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(semanticTypeId);

        if (!OwnedBy(by))
        {
            return new Outcome<BrainDirectory>(this, [], Refusal.NotOwner);
        }

        var changed = false;
        var builder = ImmutableArray.CreateBuilder<Grant>(Grants.Length);
        var factsBuilder = ImmutableArray.CreateBuilder<Signal>();

        foreach (var grant in Grants)
        {
            if (grant.WorkspaceId == BrainId
                && grant.AppId == appId
                && grant.SemanticTypeId == semanticTypeId
                && grant.Mode == mode
                && !grant.Revoked)
            {
                builder.Add(grant with { Revoked = true });
                factsBuilder.Add(new GrantRevoked(BrainId, appId, semanticTypeId, mode));
                changed = true;
            }
            else
            {
                builder.Add(grant);
            }
        }

        return changed
            ? new Outcome<BrainDirectory>(this with { Grants = builder.ToImmutable() }, factsBuilder.ToImmutable(), Refusal.None)
            : new Outcome<BrainDirectory>(this, [], Refusal.None);
    }

    /// <summary>Evaluates an authorization request and spends Once grants deterministically.</summary>
    /// <param name="caller">The calling capability caller.</param>
    /// <param name="request">The call request.</param>
    /// <returns>The aggregate outcome and the call decision.</returns>
    public (Outcome<BrainDirectory> Outcome, CallDecision Decision) Authorize(CapabilityCaller caller, CallRequest request)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(caller.AppId))
        {
            return (new Outcome<BrainDirectory>(this, [], Refusal.None), CallDecision.Allow());
        }

        var consumesOnce = false;
        var consumedIndices = new HashSet<int>();
        foreach (var type in request.SemanticTypeIds)
        {
            var matchedIndex = -1;
            for (var i = 0; i < Grants.Length; i++)
            {
                if (Grants[i].Covers(caller.AppId, type, caller.BrainId, caller.ConversationId))
                {
                    matchedIndex = i;
                    break;
                }
            }

            if (matchedIndex < 0)
            {
                return (new Outcome<BrainDirectory>(this, [], Refusal.None), CallDecision.Deny(CallDenial.MissingGrant, $"app {caller.AppId} has no grant for {type}"));
            }

            if (Grants[matchedIndex].Mode == GrantMode.Once)
            {
                consumesOnce = true;
                consumedIndices.Add(matchedIndex);
            }
        }

        if (!consumesOnce)
        {
            return (new Outcome<BrainDirectory>(this, [], Refusal.None), CallDecision.Allow());
        }

        var builder = ImmutableArray.CreateBuilder<Grant>(Grants.Length);
        var factsBuilder = ImmutableArray.CreateBuilder<Signal>();

        for (var i = 0; i < Grants.Length; i++)
        {
            if (consumedIndices.Contains(i))
            {
                var consumed = Grants[i];
                builder.Add(consumed with { Revoked = true });
                factsBuilder.Add(new GrantConsumed(consumed.WorkspaceId, consumed.AppId, consumed.SemanticTypeId));
            }
            else
            {
                builder.Add(Grants[i]);
            }
        }

        var next = this with { Grants = builder.ToImmutable() };
        return (new Outcome<BrainDirectory>(next, factsBuilder.ToImmutable(), Refusal.None), CallDecision.Allow() with { ConsumesOnce = true });
    }
}
