using Brain.Abstractions.Capabilities;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Core.Capabilities;
using Brain.Core.Modules;
using Brain.Testing.Fakes;
using Xunit;

namespace Brain.Core.Tests;

public sealed class CapabilityBrokerTests
{
    [Fact]
    public async Task CapabilityUseRequiresAnActivityContext()
    {
        var fixture = CapabilityFixture.Allowed();

        await Assert.ThrowsAsync<MissingActivityContextException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            null!,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseRequiresANondefaultStableUseName()
    {
        var fixture = CapabilityFixture.Allowed();

        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            default,
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenDelegationOmitsTheCapability()
    {
        var fixture = CapabilityFixture.Allowed();
        var context = new ActivityContext(
            fixture.Context.Workspace,
            fixture.Context.Principal,
            fixture.Context.Activity,
            fixture.Context.Correlation,
            Delegation.Empty);

        await Assert.ThrowsAsync<CapabilityNotDelegatedException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenTheWorkspaceModuleSetDoesNotContainTheDescriptor()
    {
        var fixture = CapabilityFixture.WithoutProvider();

        await Assert.ThrowsAsync<CapabilityNotInstalledException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenTheRegisteredProviderHasAnotherModuleOwner()
    {
        var fixture = CapabilityFixture.WithDifferentProviderOwner();

        await Assert.ThrowsAsync<CapabilityNotInstalledException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenThereIsNoExplicitBinding()
    {
        var fixture = CapabilityFixture.WithoutBinding();

        await Assert.ThrowsAsync<CapabilityBindingNotFoundException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenTheBindingTypesDoNotMatch()
    {
        var fixture = CapabilityFixture.Allowed();

        await Assert.ThrowsAsync<CapabilityTypeMismatchException>(() => fixture.Broker.UseAsync<OtherCapabilityInput, OtherCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new OtherCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CapabilityUseIsRefusedWhenPolicyDoesNotAllowIt()
    {
        var fixture = CapabilityFixture.Refused();

        await Assert.ThrowsAsync<CapabilityPolicyRefusedException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RetryOfTheSameCapabilityUseReturnsTheRecordedResult()
    {
        var fixture = CapabilityFixture.Allowed();

        var first = await fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken);
        var retry = await fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("changed-input-is-not-a-new-use"),
            fixture.Context,
            TestContext.Current.CancellationToken);

        Assert.Equal(first, retry);
        Assert.Equal(1, fixture.FakeClassifier.CallCount);
    }

    [Fact]
    public async Task ConcurrentDuplicateCapabilityUsesWaitForAndReturnOneRecordedResult()
    {
        var fixture = CapabilityFixture.Allowed();
        fixture.FakeClassifier.BlockNextInvocation();

        var first = fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken);
        await fixture.FakeClassifier.WaitUntilCalledAsync();
        var duplicate = fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken);

        fixture.FakeClassifier.ReleaseBlockedInvocation();
        var results = await Task.WhenAll(first, duplicate);

        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, fixture.FakeClassifier.CallCount);
    }

    [Fact]
    public async Task FailedCapabilityUseIsNotRecordedAndTheSameUseNameCanRetry()
    {
        var fixture = CapabilityFixture.Allowed();
        fixture.FakeClassifier.FailNextInvocation();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken));

        var retry = await fixture.Broker.UseAsync<ProofCapabilityInput, ProofCapabilityResult>(
            fixture.Classifier,
            new CapabilityUseName("classification/alpha"),
            new ProofCapabilityInput("alpha"),
            fixture.Context,
            TestContext.Current.CancellationToken);

        Assert.Equal("classified/alpha", retry.Classification);
        Assert.Equal(2, fixture.FakeClassifier.CallCount);
    }

    private sealed record OtherCapabilityInput(string Value);

    private sealed record OtherCapabilityResult(string Value);

    private sealed class CapabilityFixture
    {
        private CapabilityFixture(PolicyDecision decision, bool providerInstalled, ModuleId providerOwner, bool bind)
        {
            Classifier = new CapabilityDescriptor(
                new CapabilityId("proof/classify@1"),
                new ContractId("proof/classify-request@1"),
                new ContractId("proof/classify-result@1"),
                new ModuleId("proof"),
                new ContractVersion(1));
            var installedCapability = new CapabilityDescriptor(
                Classifier.Id,
                Classifier.RequestContract,
                Classifier.ResultContract,
                providerOwner,
                Classifier.Version);
            var registry = new ModuleRegistry();
            registry.Resolve(providerInstalled
                ? [Manifest(providerOwner, [installedCapability])]
                : [Manifest(new ModuleId("proof"), [])]);

            FakeClassifier = new DeterministicCapability();
            Broker = new CapabilityBroker(
                registry,
                new FixedPolicyEvaluator(decision),
                bind
                    ? new CapabilityBindingResolver([CapabilityBinding.For<ProofCapabilityInput, ProofCapabilityResult>(Classifier, FakeClassifier.InvokeAsync)])
                    : new CapabilityBindingResolver([]),
                new CapabilityUseState());
            Context = new ActivityContext(
                new WorkspaceId("workspace/sales"),
                new PrincipalId("principal/alice"),
                BrainActivityId.New(),
                new CorrelationId("correlation/classify"),
                new Delegation([], [Classifier.Id]));
        }

        public CapabilityDescriptor Classifier { get; }

        public DeterministicCapability FakeClassifier { get; }

        public CapabilityBroker Broker { get; }

        public ActivityContext Context { get; }

        public static CapabilityFixture Allowed() => new(PolicyDecision.Allowed, providerInstalled: true, new ModuleId("proof"), bind: true);

        public static CapabilityFixture Refused() => new(PolicyDecision.Refused, providerInstalled: true, new ModuleId("proof"), bind: true);

        public static CapabilityFixture WithoutProvider() => new(PolicyDecision.Allowed, providerInstalled: false, new ModuleId("proof"), bind: true);

        public static CapabilityFixture WithDifferentProviderOwner() => new(PolicyDecision.Allowed, providerInstalled: true, new ModuleId("other"), bind: true);

        public static CapabilityFixture WithoutBinding() => new(PolicyDecision.Allowed, providerInstalled: true, new ModuleId("proof"), bind: false);

        private static ModuleManifest Manifest(ModuleId owner, IReadOnlyCollection<CapabilityDescriptor> capabilities)
            => new(
                owner,
                new ModuleVersion(1, 0, 0),
                [],
                [],
                [],
                [],
                [],
                [],
                capabilities,
                []);
    }

    private sealed class FixedPolicyEvaluator(PolicyDecision decision) : IWorkspacePolicyEvaluator
    {
        public PolicyDecision AuthorizeOperation(WorkspaceContext caller, Brain.Abstractions.Operations.OperationDescriptor operation)
            => PolicyDecision.Refused;

        public PolicyDecision AuthorizeGraphChange(ActivityContext context, GraphChangeRequest request)
            => PolicyDecision.Refused;

        public PolicyDecision AuthorizeCapability(ActivityContext context, CapabilityDescriptor capability)
            => decision;
    }
}
