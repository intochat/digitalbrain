using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity;
using DigitalBrain.Sdk.Capacity;
using DigitalBrain.Sdk.Identity;
using DigitalBrain.Sdk.Integrations;
using DigitalBrain.Sdk.Secrets;
using DigitalBrain.Platform.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core.Tests.Unit;

public sealed class PlatformHostingFacts
{
    [Fact]
    public async Task ABrainWithoutModulesHasEveryPlatformFacet()
    {
        await using var brain = await UnitTest.Create().StartAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(brain.SiloServices.GetRequiredService<ICapacity>());
        Assert.NotNull(brain.SiloServices.GetRequiredService<ICapabilities>());
        Assert.NotNull(brain.SiloServices.GetRequiredService<IIdentity>());
        Assert.NotNull(brain.SiloServices.GetRequiredService<IBrainAccess>());
        Assert.NotNull(brain.SiloServices.GetRequiredService<IKeyWrapper>());
        Assert.NotNull(brain.Get<ISecrets>("owner"));
        Assert.Single(brain.SiloServices.GetServices<ICallFilterStage>(), stage => stage.GetType().Name == "GrantCallFilterStage");
    }

    [Fact]
    public async Task AddingTheBrainTwiceRegistersTheGrantStageOnce()
    {
        await using var brain = await UnitTest.Create()
            .ConfigureSilo(silo => silo.AddDigitalBrain())
            .StartAsync(TestContext.Current.CancellationToken);
        Assert.Single(brain.SiloServices.GetServices<ICallFilterStage>(), stage => stage.GetType().Name == "GrantCallFilterStage");
    }

    [Fact]
    public async Task IdentityAnswersOwnershipMembershipAndGrantQuestionsThroughTheSdk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var identity = brain.SiloServices.GetRequiredService<IIdentity>();
        Assert.Null(identity.CurrentPrincipal);
        Assert.Throws<UntrustedCallerException>(identity.RequireOwner);
        var member = await brain.Get<IIdentityDirectory>(IdentityGrains.Directory)
            .RegisterAsync("reader", "a-long-test-password", "Reader", ct);
        Assert.True(await identity.CanAccessAsync(member.PrincipalId, member.BrainId, ct));
        Assert.False(await identity.CanAccessAsync(member.PrincipalId, "another-brain", ct));
        var caller = new CallerContext
        {
            PrincipalId = member.PrincipalId, AccountId = member.AccountId, BrainId = member.BrainId,
            AppId = "reader-app", ConversationId = "chat", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy,
        };
        CallerContextStamper.Stamp(caller);
        try
        {
            Assert.Equal(caller, identity.CurrentPrincipal);
            Assert.Equal(member.PrincipalId, identity.RequireOwner());
            Assert.False(await identity.HasGrantAsync(caller, "person.email", ct));
            var store = brain.Get<IGrantStore>(IdentityGrains.Grants(member.BrainId));
            await store.GrantAsync(new Grant
            {
                AppId = caller.AppId, WorkspaceId = caller.BrainId, SemanticTypeId = "person.email",
                Mode = GrantMode.ThisChat, ConversationId = "chat",
            }, ct);
            Assert.True(await identity.HasGrantAsync(caller, "person.email", ct));
            Assert.False(await identity.HasGrantAsync(caller with { ConversationId = "other" }, "person.email", ct));
            Assert.Single(await store.ListAsync(ct));
        }
        finally { Orleans.Runtime.RequestContext.Clear(); }
    }

    [Fact]
    public async Task ABrainWithoutModulesRequiresAGrantBeforeAnAppCanRead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().StartAsync(ct);
        var caller = new CallerContext
        {
            PrincipalId = "owner", AccountId = "owner", BrainId = "owner",
            AppId = "reader", Kind = CallerKind.App, StampedBy = TrustedEdge.AppProxy,
        };
        var request = new CallRequest { Caller = caller, TargetNeuron = "person", Operation = "Read", SemanticTypeIds = ["person.email"] };
        var filter = brain.SiloServices.GetRequiredService<ICallFilter>();
        Assert.Equal(CallDenial.MissingGrant, (await filter.AuthorizeAsync(request, ct)).Denial);
        await brain.Get<IGrantStore>(IdentityGrains.Grants("owner")).GrantAsync(new Grant
        {
            AppId = "reader", WorkspaceId = "owner", SemanticTypeId = "person.email", Mode = GrantMode.Once,
        }, ct);
        Assert.True((await filter.AuthorizeAsync(request, ct)).Allowed);
        Assert.Equal(CallDenial.MissingGrant, (await filter.AuthorizeAsync(request, ct)).Denial);
    }
}
