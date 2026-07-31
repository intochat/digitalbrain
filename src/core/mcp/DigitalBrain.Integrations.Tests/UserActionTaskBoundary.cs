using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

public sealed class UserActionTaskBoundary(AuthorizationRailFixture fixture)
{
    [Fact(DisplayName =
        "missing authorization maps through production ModuleUserActionBoundary custody without provider call or shared accounts")]
    public async Task MissingAuthorizationMapsToUserActionWithoutProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        CatalogGmail(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var driver = test.Neuron<IIntegrationDriver>("user-action-boundary");
        var sessionsBefore = test.Mcp().SessionCount;

        var requiredException = await Assert.ThrowsAsync<McpAuthorizationRequiredException>(() =>
            driver.Reference.ReadGmailMessage(
                commandId,
                IntegrationsFixture.SampleGmailAccount,
                IntegrationsFixture.SampleMessageId,
                cancellationToken));

        Assert.NotNull(requiredException.Requirement);
        Assert.Equal(sessionsBefore, test.Mcp().SessionCount);

        var taskId = NeuronId.For<ITask>(driver.Id.Owner, "user-action-boundary-task");
        var attempt = new AttemptId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var module = NeuronId.For<IIntegrationDriver>(driver.Id.Owner, driver.Id.Name);
        var actionEpoch = Guid.NewGuid();
        var completer = UserActionCompletionBridge.For(taskId.Owner, actionEpoch);
        var custody = new MemoryUserActionCustody(TimeProvider.System);

        var issued = await ModuleUserActionBoundary.IssueFromAuthorizationRequiredAsync(
            custody,
            taskId.Owner,
            taskId,
            attempt,
            module,
            requiredException.Requirement.ServerKey,
            requiredException.Requirement.ServerDisplayName,
            requiredException.Requirement.SignInUrl,
            requiredException.Requirement.State,
            parkRevision: 0,
            lifetime: TimeSpan.FromHours(1),
            completer,
            actionEpoch,
            cancellationToken);

        Assert.Equal(requiredException.Requirement.ServerKey, issued.Requirement.ModuleId);
        Assert.Equal(module, issued.Requirement.Module);
        Assert.Equal(completer, issued.Requirement.Completer);
        Assert.NotEqual(Guid.Empty, issued.Requirement.ActionEpoch);

        var surface = ModuleUserActionBoundary.SerializeSafeSurface(issued.Requirement);
        Assert.False(ModuleUserActionBoundary.SurfaceContainsSecretFragments(surface));
        Assert.DoesNotContain(requiredException.Requirement.State, surface, StringComparison.Ordinal);
        Assert.DoesNotContain(
            requiredException.Requirement.SignInUrl.AbsoluteUri,
            surface,
            StringComparison.Ordinal);
        Assert.Contains(requiredException.Requirement.ServerKey, surface, StringComparison.Ordinal);

        Assert.True(custody.TryLoadActionMaterial(issued.Requirement.ActionReference, out var material));
        Assert.NotEmpty(material);
        Assert.Contains("state"u8.ToArray(), material.AsSpan());
        Assert.DoesNotContain("authorityProof"u8.ToArray(), material.AsSpan());

        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.AccountRegistry"));
        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.IAccount"));
        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.SharedAccount"));

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Single(requiredFacts);
        Assert.Equal(requiredException.Requirement.ServerKey, issued.Requirement.ModuleId);
    }

    private static void CatalogGmail(TestBrain test)
        => test.Mcp().Catalog(
            IntegrationsFixture.GmailServerKey,
            AdmittedMcpTools.GmailGetMessage(
                id: IntegrationsFixture.SampleMessageId,
                subject: IntegrationsFixture.SampleSubject,
                sender: IntegrationsFixture.SampleSender,
                plaintextBody: IntegrationsFixture.SampleBody));
}
