using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Google;
using DigitalBrain.Mcp;
using DigitalBrain.Mcp.Testing;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Integrations.Tests;

[Collection(GmailFakeHostTestGroup.Name)]
public sealed class UserActionTaskBoundary(AuthorizationRailFixture fixture)
{
    [Fact(DisplayName =
        "missing authorization maps through production ModuleUserActionBoundary custody without provider call or shared accounts")]
    public async Task MissingAuthorizationMapsToUserActionWithoutProviderCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        GmailHelpers.CatalogSampleMessage(test);

        var commandId = CommandId.New();
        var auth = test.Neuron<IMcpAuthorization>(McpAuthorizationNeuron.InstanceName);
        var sessionsBefore = test.Mcp().SessionCount;
        var requiredWait = auth.Outgoing.NextAsync<AuthorizationRequired>(cancellationToken);

        using var hang = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        hang.CancelAfter(TimeSpan.FromSeconds(15));
        var send = test.Client.Get<IGmail>(IntegrationsFixture.SampleGmailAccount)
            .SendAsync(
                new GmailRequest($"Read Gmail message {IntegrationsFixture.SampleMessageId}", commandId),
                hang.Token);

        var required = (await requiredWait).Synapse;
        Assert.Equal(sessionsBefore, test.Mcp().SessionCount);

        var taskId = NeuronId.For<ITask>(test.Client.Owner, "user-action-boundary-task");
        var attempt = new AttemptId(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));
        var module = NeuronId.For<IGmail>(test.Client.Owner, IntegrationsFixture.SampleGmailAccount);
        var actionEpoch = Guid.NewGuid();
        var completer = UserActionCompletionBridge.For(taskId.Owner, actionEpoch);
        var custody = new MemoryUserActionCustody(TimeProvider.System);

        var issued = await ModuleUserActionBoundary.IssueFromAuthorizationRequiredAsync(
            custody,
            taskId.Owner,
            taskId,
            attempt,
            module,
            required.ServerKey,
            required.ServerDisplayName,
            required.SignInUrl,
            required.State,
            parkRevision: 0,
            lifetime: TimeSpan.FromHours(1),
            completer,
            actionEpoch,
            cancellationToken);

        Assert.Equal(required.ServerKey, issued.Requirement.ModuleId);
        Assert.Equal(module, issued.Requirement.Module);
        Assert.Equal(completer, issued.Requirement.Completer);
        Assert.NotEqual(Guid.Empty, issued.Requirement.ActionEpoch);

        var surface = ModuleUserActionBoundary.SerializeSafeSurface(issued.Requirement);
        Assert.False(ModuleUserActionBoundary.SurfaceContainsSecretFragments(surface));
        Assert.DoesNotContain(required.State, surface, StringComparison.Ordinal);
        Assert.DoesNotContain(
            required.SignInUrl.AbsoluteUri,
            surface,
            StringComparison.Ordinal);
        Assert.Contains(required.ServerKey, surface, StringComparison.Ordinal);

        Assert.True(custody.TryLoadActionMaterial(issued.Requirement.ActionReference, out var material));
        Assert.NotEmpty(material);
        Assert.Contains("state"u8.ToArray(), material.AsSpan());
        Assert.DoesNotContain("authorityProof"u8.ToArray(), material.AsSpan());

        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.AccountRegistry"));
        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.IAccount"));
        Assert.Null(typeof(UserActionRequired).Assembly.GetType("DigitalBrain.Tasks.SharedAccount"));

        var requiredFacts = await auth.Outgoing.ReadAsync<AuthorizationRequired>(afterSequence: 0, cancellationToken);
        Assert.Single(requiredFacts);
        Assert.Equal(required.ServerKey, issued.Requirement.ModuleId);

        await hang.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }
}
