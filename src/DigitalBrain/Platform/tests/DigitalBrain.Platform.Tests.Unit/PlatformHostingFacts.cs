using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Secrets;
using DigitalBrain.Platform.Identity.Authority;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Platform.Tests.Unit;

public sealed class PlatformHostingFacts
{
    [Fact]
    public async Task ABrainWithoutModulesHasEveryPlatformFacet()
    {
        await using var brain = await ModuleTest.Create().StartAsync(TestContext.Current.CancellationToken);
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
        await using var brain = await ModuleTest.Create()
            .ConfigureSilo(silo => silo.AddDigitalBrain())
            .StartAsync(TestContext.Current.CancellationToken);
        Assert.Single(brain.SiloServices.GetServices<ICallFilterStage>(), stage => stage.GetType().Name == "GrantCallFilterStage");
    }

    [Fact]
    public async Task IdentityAnswersOwnershipMembershipAndGrantQuestionsThroughTheSdk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var identity = brain.SiloServices.GetRequiredService<IIdentity>();
        Assert.Null(identity.CurrentPrincipal);
        Assert.Throws<UntrustedCallerException>(identity.RequirePrincipal);
        var member = await brain.Get<IIdentityDirectory>(IdentityGrains.Directory)
            .RegisterAsync("reader", "a-long-test-password", "Reader", ct);
        Assert.True(await identity.CanAccessAsync(member.PrincipalId, member.AccountId, member.BrainId, ct));
        Assert.False(await identity.CanAccessAsync(member.PrincipalId, member.AccountId, "another-brain", ct));
        var caller = new CallerContext
        {
            PrincipalId = member.PrincipalId,
            AccountId = member.AccountId,
            BrainId = member.BrainId,
            AppId = "reader-app",
            ConversationId = "chat",
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
        };
        CallerContextStamper.Stamp(caller);
        try
        {
            Assert.Equal(caller, identity.CurrentPrincipal);
            Assert.Equal(member.PrincipalId, identity.RequirePrincipal());
            Assert.False(await identity.HasGrantAsync(caller, "person.email", ct));
            var store = brain.Get<IBrainAuthority>(BrainScope.Create(member.AccountId, member.BrainId).Id);
            await store.Grant(new Grant
            {
                AppId = caller.AppId,
                WorkspaceId = caller.BrainId,
                SemanticTypeId = "person.email",
                Mode = GrantMode.ThisChat,
                ConversationId = "chat",
            });
            Assert.True(await identity.HasGrantAsync(caller, "person.email", ct));
            Assert.False(await identity.HasGrantAsync(caller with { ConversationId = "other" }, "person.email", ct));
            Assert.Single(await store.ListGrants());
        }
        finally { Orleans.Runtime.RequestContext.Clear(); }
    }

    [Fact]
    public async Task ABrainWithoutModulesRequiresAGrantBeforeAnAppCanRead()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await ModuleTest.Create().StartAsync(ct);
        var caller = new CallerContext
        {
            PrincipalId = "owner",
            AccountId = "owner",
            BrainId = "owner",
            AppId = "reader",
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
        };
        var authority = brain.Get<IBrainAuthority>(BrainScope.Create("owner", "owner").Id);
        await authority.Initialize("owner", "owner");
        var request = new CallRequest { Caller = caller, TargetNeuron = "person", Operation = "Read", SemanticTypeIds = ["person.email"] };
        var filter = brain.SiloServices.GetRequiredService<ICallFilter>();
        Assert.Equal(CallDenial.MissingGrant, (await filter.AuthorizeAsync(request, ct)).Denial);
        await authority.Grant(new Grant
        {
            AppId = "reader",
            WorkspaceId = "owner",
            SemanticTypeId = "person.email",
            Mode = GrantMode.Once,
        });
        Assert.True((await filter.AuthorizeAsync(request, ct)).Allowed);
        Assert.Equal(CallDenial.MissingGrant, (await filter.AuthorizeAsync(request, ct)).Denial);
    }
}
