namespace DigitalBrain.Capabilities.Identity.Model;

using System.Collections.Immutable;
using DigitalBrain;

/// <summary>A registered principal record.</summary>
public sealed record PrincipalRecord
{
    /// <summary>The principal identifier.</summary>
    public string PrincipalId { get; init; } = "";

    /// <summary>The principal's default owner membership.</summary>
    public Member? DefaultMembership { get; init; }

    /// <summary>Whether registration has completed.</summary>
    public bool RegistrationComplete { get; init; }

    /// <summary>Initializes a new instance of <see cref="PrincipalRecord"/>.</summary>
    public PrincipalRecord() { }

    /// <summary>Initializes a new instance of <see cref="PrincipalRecord"/>.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="defaultMembership">The default owner membership.</param>
    /// <param name="registrationComplete">Whether registration is complete.</param>
    public PrincipalRecord(string principalId, Member? defaultMembership, bool registrationComplete = false)
    {
        PrincipalId = principalId;
        DefaultMembership = defaultMembership;
        RegistrationComplete = registrationComplete;
    }
}

/// <summary>An idempotent brain-creation operation record.</summary>
public sealed record BrainCreation
{
    /// <summary>The account identifier.</summary>
    public string AccountId { get; init; } = "";

    /// <summary>The operation identifier.</summary>
    public string OperationId { get; init; } = "";

    /// <summary>The provisioned owner membership.</summary>
    public required Member Member { get; init; }

    /// <summary>Initializes a new instance of <see cref="BrainCreation"/>.</summary>
    public BrainCreation() { }

    /// <summary>Initializes a new instance of <see cref="BrainCreation"/>.</summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="operationId">The operation identifier.</param>
    /// <param name="member">The provisioned owner membership.</param>
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public BrainCreation(string accountId, string operationId, Member member)
    {
        AccountId = accountId;
        OperationId = operationId;
        Member = member;
    }
}

/// <summary>Pure domain model governing accounts, principals, and brain creation operations.</summary>
public sealed record AccountBook
{
    /// <summary>All accounts.</summary>
    public ImmutableArray<Account> Accounts { get; init; } = [];

    /// <summary>All registered principals.</summary>
    public ImmutableArray<PrincipalRecord> Principals { get; init; } = [];

    /// <summary>All brain creation operations.</summary>
    public ImmutableArray<BrainCreation> Operations { get; init; } = [];

    /// <summary>Finds an account by identifier.</summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The account, or null if not found.</returns>
    public Account? AccountOf(string accountId)
        => Accounts.FirstOrDefault(a => a.AccountId == accountId);

    /// <summary>Finds a principal record by identifier.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns>The principal record, or null if not found.</returns>
    public PrincipalRecord? PrincipalOf(string principalId)
        => Principals.FirstOrDefault(p => p.PrincipalId == principalId);

    /// <summary>Ensures an account exists.</summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="name">The display name.</param>
    /// <param name="ownerPrincipalId">The owning principal identifier.</param>
    /// <param name="createdAt">The creation timestamp if known.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The updated account book, account, and emitted domain facts.</returns>
    public (AccountBook Next, Account Account, ImmutableArray<Signal> Facts) EnsureAccount(
        string accountId,
        string name,
        string ownerPrincipalId,
        DateTimeOffset createdAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        if (AccountOf(accountId) is { } existing)
        {
            return (this, existing, []);
        }

        var account = new Account
        {
            AccountId = accountId,
            Name = name,
            OwnerPrincipalId = ownerPrincipalId,
            CreatedAt = createdAt == default ? now : createdAt,
        };
        var next = this with { Accounts = Accounts.Add(account) };
        return (next, account, [new AccountEnsured(account)]);
    }

    /// <summary>Creates a new brain idempotently, scoped by both account identifier and operation identifier.</summary>
    /// <param name="accountId">The owning account identifier.</param>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="operationId">The client operation identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="kind">The brain kind.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The updated account book, the provision result, and emitted domain facts.</returns>
    public (AccountBook Next, BrainProvision Provision, ImmutableArray<Signal> Facts) CreateBrain(
        string accountId,
        string principalId,
        string operationId,
        string displayName,
        BrainKind kind,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        if (Operations.FirstOrDefault(op => op.AccountId == accountId && op.OperationId == operationId) is { } existing)
        {
            return (this, new BrainProvision(existing.Member, false), []);
        }

        var (bookWithAccount, _, accountFacts) = EnsureAccount(accountId, displayName, principalId, default, now);
        var brainId = $"brain-{Guid.NewGuid():N}";
        var owner = new Member
        {
            PrincipalId = principalId,
            AccountId = accountId,
            BrainId = brainId,
            Role = MemberRole.Owner,
            DisplayName = displayName,
            JoinedAt = now,
        };

        var creation = new BrainCreation(accountId, operationId, owner);
        var next = bookWithAccount with { Operations = bookWithAccount.Operations.Add(creation) };
        var facts = ImmutableArray.Create<Signal>(
            [.. accountFacts,
            new BrainCreated(brainId, accountId, kind),
            new MemberAdded(owner)]);

        return (next, new BrainProvision(owner, true), facts);
    }

    /// <summary>Registers a principal idempotently.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The updated account book, the provision result, and emitted domain facts.</returns>
    public (AccountBook Next, BrainProvision Provision, ImmutableArray<Signal> Facts) RegisterPrincipal(
        string principalId,
        string displayName,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        if (PrincipalOf(principalId) is { RegistrationComplete: true, DefaultMembership: { } existing })
        {
            return (this, new BrainProvision(existing, false), []);
        }

        var accountId = $"account-{principalId}";
        var brainId = $"brain-{principalId}";
        var name = string.IsNullOrWhiteSpace(displayName) ? principalId : displayName;

        var (bookWithAccount, _, accountFacts) = EnsureAccount(accountId, name, principalId, now, now);
        var owner = new Member
        {
            PrincipalId = principalId,
            AccountId = accountId,
            BrainId = brainId,
            Role = MemberRole.Owner,
            DisplayName = name,
            JoinedAt = now,
        };

        var row = new PrincipalRecord(principalId, owner, true);
        var remaining = bookWithAccount.Principals.Where(p => p.PrincipalId != principalId).ToImmutableArray();
        var next = bookWithAccount with { Principals = remaining.Add(row) };

        var facts = ImmutableArray.Create<Signal>(
            [.. accountFacts,
            new BrainCreated(brainId, accountId, BrainKind.Personal),
            new MemberAdded(owner),
            new PrincipalRegistered(principalId, owner)]);

        return (next, new BrainProvision(owner, true), facts);
    }

    /// <summary>Completes registration for a principal.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns>The default brain provision if registered, otherwise null.</returns>
    public BrainProvision? CompleteRegistration(string principalId)
        => PrincipalOf(principalId) is { RegistrationComplete: true, DefaultMembership: { } member }
            ? new BrainProvision(member, false)
            : null;

    /// <summary>Checks whether a principal is registered.</summary>
    /// <param name="principalId">The principal identifier.</param>
    /// <returns>True if registered.</returns>
    public bool IsRegistered(string principalId)
        => PrincipalOf(principalId)?.RegistrationComplete == true;
}
