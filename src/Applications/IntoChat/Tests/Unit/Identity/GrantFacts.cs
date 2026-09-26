using DigitalBrain.Contracts.Enforcement;
using IntoChat.Identity;
using IntoChat.Identity.Grants;
using DigitalBrain.Testing.Unit;
using Xunit;

namespace IntoChat.Tests.Unit.Identity;

public sealed class GrantFacts
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AnAppCallerNeedsAGrantForEverySemanticType()
    {
        var request = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate");
        var denied = GrantRules.Evaluate(request, null, []);
        Assert.Equal(CallDenial.MissingGrant, denied!.Denial);

        var allowed = GrantRules.Evaluate(request, null, [Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1")]);
        Assert.True(allowed!.Allowed);
    }

    [Fact]
    public void ARevokedOrForeignGrantLooksMissing()
    {
        var request = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate");
        Assert.Equal(CallDenial.MissingGrant,
            GrantRules.Evaluate(request, null, [Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1") with { Revoked = true }])!.Denial);
        Assert.Equal(CallDenial.MissingGrant,
            GrantRules.Evaluate(request, null, [Grant(GrantMode.Always, "person.birthDate", "app-2", "ws-1")])!.Denial);
        Assert.Equal(CallDenial.MissingGrant,
            GrantRules.Evaluate(request, null, [Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-2")])!.Denial);
    }

    [Fact]
    public void AThisChatGrantNeedsTheMatchingConversation()
    {
        var request = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate") with
        {
            Caller = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate").Caller with { ConversationId = "chat-1" },
        };
        var matching = Grant(GrantMode.ThisChat, "person.birthDate", "app-1", "ws-1") with { ConversationId = "chat-1" };
        var other = Grant(GrantMode.ThisChat, "person.birthDate", "app-1", "ws-1") with { ConversationId = "chat-2" };
        Assert.True(GrantRules.Evaluate(request, null, [matching])!.Allowed);
        Assert.Equal(CallDenial.MissingGrant, GrantRules.Evaluate(request, null, [other])!.Denial);
    }

    [Fact]
    public void UserAccessInsideItsOwnWorkspaceNeedsNoGrant()
    {
        var request = Request(CallerKind.User, null, "ws-1", "person.birthDate");
        var member = new Member { PrincipalId = "owner", AccountId = "a", WorkspaceId = "ws-1", Role = MemberRole.Owner, DisplayName = "Owner", JoinedAt = Now };
        Assert.Null(GrantRules.Evaluate(request, member, []));
    }

    [Fact]
    public void AMemberOfAnotherWorkspaceIsDenied()
    {
        var request = Request(CallerKind.User, null, "ws-1", "person.birthDate");
        var member = new Member { PrincipalId = "owner", AccountId = "a", WorkspaceId = "ws-2", Role = MemberRole.Owner, DisplayName = "Owner", JoinedAt = Now };
        Assert.Equal(CallDenial.OutsideWorkspace, GrantRules.Evaluate(request, member, [])!.Denial);
    }

    [Fact]
    public async Task TheStageFetchesMembershipAndGrantsThenAppliesTheRules()
    {
        var stage = new GrantCallFilterStage(new FakePolicy(
            new Member { PrincipalId = "owner", AccountId = "a", WorkspaceId = "ws-1", Role = MemberRole.Owner, DisplayName = "Owner", JoinedAt = Now },
            [Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-2")]));
        var request = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate");
        var decision = await stage.EvaluateAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(CallDenial.MissingGrant, decision!.Denial);
    }

    [Fact]
    public async Task AOnceGrantIsConsumedAfterOneSuccessfulRead()
    {
        var policy = new RecordingPolicy([Grant(GrantMode.Once, "person.birthDate", "app-1", "ws-1")]);
        var stage = new GrantCallFilterStage(policy);
        var decision = await stage.EvaluateAsync(Request(CallerKind.App, "app-1", "ws-1", "person.birthDate"), TestContext.Current.CancellationToken);

        Assert.True(decision!.Allowed);
        var spent = Assert.Single(policy.Consumed);
        Assert.Equal("person.birthDate", spent.SemanticTypeId);
        Assert.Equal(GrantMode.Once, spent.Mode);
    }

    [Fact]
    public async Task AnAlwaysGrantIsNotConsumed()
    {
        var policy = new RecordingPolicy([Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1")]);
        var stage = new GrantCallFilterStage(policy);
        var decision = await stage.EvaluateAsync(Request(CallerKind.App, "app-1", "ws-1", "person.birthDate"), TestContext.Current.CancellationToken);

        Assert.True(decision!.Allowed);
        Assert.Empty(policy.Consumed);
    }

    [Fact]
    public async Task ADeniedReadConsumesNoGrant()
    {
        var policy = new RecordingPolicy([Grant(GrantMode.Once, "person.birthDate", "app-1", "ws-2")]);
        var stage = new GrantCallFilterStage(policy);
        var decision = await stage.EvaluateAsync(Request(CallerKind.App, "app-1", "ws-1", "person.birthDate"), TestContext.Current.CancellationToken);

        Assert.Equal(CallDenial.MissingGrant, decision!.Denial);
        Assert.Empty(policy.Consumed);
    }

    [Fact]
    public async Task ConsumingAOnceGrantMakesTheNextReadLookEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var store = brain.Get<IGrantStore>(IdentityGrains.Grants("ws-1"));
        await store.GrantAsync(Grant(GrantMode.Once, "person.birthDate", "app-1", "ws-1"), ct);
        Assert.Single(await store.ListAsync(ct));

        await store.RevokeAsync("app-1", "person.birthDate", GrantMode.Once, ct);
        Assert.Empty(await store.ListAsync(ct));

        var decision = GrantRules.Evaluate(
            Request(CallerKind.App, "app-1", "ws-1", "person.birthDate"), null, await store.ListAsync(ct));
        Assert.Equal(CallDenial.MissingGrant, decision!.Denial);
        Assert.Contains("missing", decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GrantStoreGrantsRevokesAndRelists()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var store = brain.Get<IGrantStore>(IdentityGrains.Grants("ws-1"));
        await store.GrantAsync(Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1"), ct);
        Assert.Single(await store.ListAsync(ct));
        await store.RevokeAsync("app-1", "person.birthDate", GrantMode.Always, ct);
        Assert.Empty(await store.ListAsync(ct));
    }

    [Fact]
    public async Task RevokingOneModeKeepsTheOthersAndARevokedValueLooksEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<IdentityModule>().StartAsync(ct);
        var store = brain.Get<IGrantStore>(IdentityGrains.Grants("ws-1"));
        await store.GrantAsync(Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1"), ct);
        await store.GrantAsync(Grant(GrantMode.ThisChat, "person.birthDate", "app-1", "ws-1") with { ConversationId = "chat-1" }, ct);
        Assert.Equal(2, (await store.ListAsync(ct)).Count);

        await store.RevokeAsync("app-1", "person.birthDate", GrantMode.Always, ct);
        var remaining = Assert.Single(await store.ListAsync(ct));
        Assert.Equal(GrantMode.ThisChat, remaining.Mode);

        var request = Request(CallerKind.App, "app-1", "ws-1", "person.birthDate");
        var revoked = Grant(GrantMode.Always, "person.birthDate", "app-1", "ws-1") with { Revoked = true };
        var decision = GrantRules.Evaluate(request, null, [revoked]);
        Assert.Equal(CallDenial.MissingGrant, decision!.Denial);
        Assert.Contains("missing", decision.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    private static Grant Grant(GrantMode mode, string semanticTypeId, string appId, string workspaceId) => new()
    {
        AppId = appId,
        SemanticTypeId = semanticTypeId,
        Mode = mode,
        WorkspaceId = workspaceId,
        GrantedAt = Now,
    };

    private static CallRequest Request(CallerKind kind, string? appId, string workspaceId, params string[] semanticTypes) => new()
    {
        Caller = new CallerContext
        {
            PrincipalId = "principal-1",
            AccountId = "account-1",
            WorkspaceId = workspaceId,
            Kind = kind,
            StampedBy = kind == CallerKind.App ? TrustedEdge.AppProxy : TrustedEdge.AuthenticatedHttp,
            AppId = appId,
        },
        TargetNeuron = "neuron-1",
        Operation = "Read",
        SemanticTypeIds = semanticTypes,
    };

    private sealed class FakePolicy(Member? member, IReadOnlyList<Grant> grants) : IGrantPolicySource
    {
        public ValueTask<Member?> FindMemberAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(member);

        public ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(grants);

        public ValueTask ConsumeOnceAsync(CallerContext caller, IReadOnlyList<Grant> consumed, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private sealed class RecordingPolicy(IReadOnlyList<Grant> grants) : IGrantPolicySource
    {
        public List<Grant> Consumed { get; } = [];

        public ValueTask<Member?> FindMemberAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult<Member?>(null);

        public ValueTask<IReadOnlyList<Grant>> ListGrantsAsync(CallerContext caller, CancellationToken cancellationToken)
            => ValueTask.FromResult(grants);

        public ValueTask ConsumeOnceAsync(CallerContext caller, IReadOnlyList<Grant> consumed, CancellationToken cancellationToken)
        {
            Consumed.AddRange(consumed);
            return ValueTask.CompletedTask;
        }
    }
}
