using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Authentication;
using Orleans.Hosting;
using Orleans.Journaling;
using Orleans.Serialization;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Simulations;

public sealed class AccountEnrichmentCompositionContracts
{
    [Fact(DisplayName = "Salesforce binds approval to exact arguments and never retries uncertainty")]
    public async Task SalesforceBindsApprovalToExactArgumentsAndNeverRetriesUncertainty()
    {
        var gmail = new RecordingGmailTransport();
        var transport = new RecordingSalesforceTransport { FailCalls = true };
        var cluster = await StartClusterAsync(gmail, transport);

        try
        {
            var owner = new OwnerId("uncertain-salesforce");
            var verifier = NeuronId.For<SalesforceMutationVerifier>(owner, "verifier");
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
            var command = new CommandId(Guid.Parse("e412132c-7273-42d0-90df-a28a3fb00f69"));
            await session.FireAsync(verifier, new VerifySalesforceMutation(
                command,
                "001000000000042AAA",
                "Approved description"));

            var delivery = await ReadUntilAsync<SalesforceMutationVerified>(session, verifier);
            var verified = Assert.IsType<SalesforceMutationVerified>(delivery.Synapse);

            Assert.Equal(SalesforceMutationState.AwaitingApproval, verified.ProposedState);
            Assert.True(verified.WrongFingerprintRejected);
            Assert.True(verified.DifferentArgumentsRejected);
            Assert.Equal(SalesforceMutationState.OutcomeUncertain, verified.ApprovedState);
            Assert.True(verified.ReplayReturnedSameReceipt);
            Assert.Single(transport.Calls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "compiled typed composition requires exact approval before enriching one Salesforce Account")]
    public async Task CompiledTypedCompositionRequiresExactApprovalBeforeEnrichingOneSalesforceAccount()
    {
        var gmail = new RecordingGmailTransport();
        var salesforce = new RecordingSalesforceTransport();
        var cluster = await StartClusterAsync(gmail, salesforce);

        try
        {
            var owner = new OwnerId("account-enrichment");
            var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
            var command = new EnrichAccountFromEmail(
                new CommandId(Guid.Parse("2f65dd7a-c6fd-413b-8b38-16062368de2c")),
                "gmail-message-42",
                "001000000000042AAA");

            await session.FireAsync(composition, command);

            var proposedDelivery = await ReadUntilAsync<AccountEnrichmentProposed>(session, composition);
            var proposed = Assert.IsType<AccountEnrichmentProposed>(proposedDelivery.Synapse);

            Assert.Equal(command.CommandId, proposed.CommandId);
            Assert.Equal(command.MessageId, proposed.MessageId);
            Assert.Equal(command.AccountId, proposed.AccountId);
            Assert.Equal(
                "Email from priya@northstar.example: Pilot rollout\nWe are ready to start the pilot on Monday.",
                proposed.Description);
            Assert.NotEqual(string.Empty, proposed.Fingerprint);
            Assert.Empty(salesforce.Calls);

            var approval = new ApproveAccountEnrichment(
                command.CommandId,
                command.MessageId,
                proposed.Fingerprint);
            await session.FireAsync(composition, approval);

            var completed = await ReadUntilAsync<AccountEnriched>(session, composition);
            var outcome = Assert.IsType<AccountEnriched>(completed.Synapse);

            Assert.Equal(command.CommandId, outcome.CommandId);
            Assert.Equal(command.MessageId, outcome.MessageId);
            Assert.Equal(command.AccountId, outcome.AccountId);
            Assert.Equal(
                "Email from priya@northstar.example: Pilot rollout\nWe are ready to start the pilot on Monday.",
                outcome.Description);

            var gmailCall = Assert.Single(gmail.Calls);
            Assert.Equal(new Uri("https://gmailmcp.googleapis.com/mcp/v1"), gmailCall.Endpoint);
            Assert.Equal("fake-gmail-token", gmailCall.AccessToken);
            Assert.Equal("get_message", gmailCall.Tool);
            Assert.Equal("gmail-message-42", gmailCall.Arguments["messageId"]);
            Assert.Equal("FULL_CONTENT", gmailCall.Arguments["messageFormat"]);

            var salesforceCall = Assert.Single(salesforce.Calls);
            Assert.Equal(
                new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
                salesforceCall.Endpoint);
            Assert.Equal("fake-salesforce-token", salesforceCall.AccessToken);
            Assert.Equal("update_sobject_record", salesforceCall.Tool);
            Assert.Equal("Account", salesforceCall.Arguments["sobject-name"]);
            Assert.Equal(command.AccountId, salesforceCall.Arguments["id"]);

            var body = Assert.IsType<Dictionary<string, object?>>(salesforceCall.Arguments["body"]);
            Assert.Equal(outcome.Description, body["Description"]);

            await session.FireAsync(composition, approval);
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

            Assert.Single(salesforce.Calls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(
        RecordingGmailTransport gmail,
        RecordingSalesforceTransport salesforce)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.AddDigitalBrain("account-enrichment");
            GoogleModule.Configure(silo);
            SalesforceModule.Configure(silo);
            silo.Services.AddSingleton<IGoogleMcpAuthorization>(
                new FakeGoogleAuthorization("fake-gmail-token"));
            silo.Services.AddSingleton<IGmailMcpTransport>(gmail);
            silo.Services.AddSingleton<ISalesforceMcpAuthorization>(
                new FakeSalesforceAuthorization("fake-salesforce-token"));
            silo.Services.AddSingleton<ISalesforceMcpTransport>(salesforce);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton<IJournalStorageProvider>(new VolatileJournalStorageProvider());
        });
        builder.ConfigureClient(client =>
        {
            client.Services.AddSerializer(serializer => serializer.AddJsonSerializer(
                type => type == typeof(ChatMessage) || type == typeof(ChatResponse)));
        });

        var cluster = builder.Build();
        await cluster.DeployAsync();

        return cluster;
    }

    private static async Task<SynapseDelivery> ReadUntilAsync<TSynapse>(
        ISessionNeuron session,
        NeuronId neuron)
        where TSynapse : Synapse
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var journal = await session.ReadNeuronJournalAsync(
                neuron,
                JournalKind.Outgoing,
                afterSequence: 0);
            var delivery = journal.Delta.SingleOrDefault(entry => entry.Synapse is TSynapse);

            if (delivery is not null)
            {
                return delivery;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"Neuron '{neuron}' did not emit {typeof(TSynapse).Name}.");
    }
}

internal sealed class FakeGoogleAuthorization(string accessToken) : IGoogleMcpAuthorization
{
    public ClientOAuthOptions CreateOptions() => FakeOAuth.Options(accessToken);
}

internal sealed class FakeSalesforceAuthorization(string accessToken) : ISalesforceMcpAuthorization
{
    public ClientOAuthOptions CreateOptions() => FakeOAuth.Options(accessToken);
}

internal static class FakeOAuth
{
    internal static ClientOAuthOptions Options(string accessToken) => new()
    {
        ClientId = "fake-client",
        RedirectUri = new Uri("http://localhost/fake-callback"),
        TokenCache = new TokenCache(accessToken),
    };

    private sealed class TokenCache(string accessToken) : ITokenCache
    {
        public ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<TokenContainer?>(new()
            {
                AccessToken = accessToken,
                TokenType = "Bearer",
                ObtainedAt = DateTimeOffset.UtcNow,
                ExpiresIn = 3600,
            });

        public ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }
}

internal sealed class RecordingGmailTransport : IGmailMcpTransport
{
    private readonly ConcurrentQueue<RecordedMcpCall> _calls = new();

    internal IReadOnlyList<RecordedMcpCall> Calls => [.. _calls];

    public async ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var tokens = await authorization.TokenCache!.GetTokensAsync(cancellationToken);
        _calls.Enqueue(new(endpoint, tokens!.AccessToken, tool, arguments));

        return JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
            subject = "Pilot rollout",
            sender = "priya@northstar.example",
            plaintextBody = "We are ready to start the pilot on Monday.",
        });
    }
}

internal sealed class RecordingSalesforceTransport : ISalesforceMcpTransport
{
    private readonly ConcurrentQueue<RecordedMcpCall> _calls = new();

    internal IReadOnlyList<RecordedMcpCall> Calls => [.. _calls];

    internal bool FailCalls { get; init; }

    public async ValueTask<JsonElement> CallToolAsync(
        Uri endpoint,
        ClientOAuthOptions authorization,
        string tool,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var tokens = await authorization.TokenCache!.GetTokensAsync(cancellationToken);
        _calls.Enqueue(new(endpoint, tokens!.AccessToken, tool, arguments));

        if (FailCalls)
        {
            throw new HttpRequestException("Simulated loss after Salesforce invocation began.");
        }

        return JsonSerializer.SerializeToElement(new { success = true });
    }
}

internal sealed record RecordedMcpCall(
    Uri Endpoint,
    string AccessToken,
    string Tool,
    IReadOnlyDictionary<string, object?> Arguments);

[GenerateSerializer]
[Alias("db.test.verify-salesforce-mutation")]
internal sealed record VerifySalesforceMutation(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string AccountId,
    [property: Id(2)] string Description) : Synapse;

[GenerateSerializer]
[Alias("db.test.salesforce-mutation-verified")]
internal sealed record SalesforceMutationVerified(
    [property: Id(0)] SalesforceMutationState ProposedState,
    [property: Id(1)] bool WrongFingerprintRejected,
    [property: Id(2)] bool DifferentArgumentsRejected,
    [property: Id(3)] SalesforceMutationState ApprovedState,
    [property: Id(4)] bool ReplayReturnedSameReceipt) : Synapse;

internal sealed class SalesforceMutationVerifier : Neuron,
    IHandle<VerifySalesforceMutation>,
    IEmit<SalesforceMutationVerified>
{
    public async Task HandleAsync(
        VerifySalesforceMutation synapse,
        CancellationToken cancellationToken)
    {
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var proposal = await salesforce.ProposeAccountDescriptionAsync(
            synapse.CommandId,
            synapse.AccountId,
            synapse.Description);
        var wrongFingerprintRejected = await RejectsAsync(
            () => salesforce.ApproveAccountDescriptionAsync(
                synapse.CommandId,
                "WRONG-FINGERPRINT"));
        var differentArgumentsRejected = await RejectsAsync(
            () => salesforce.ProposeAccountDescriptionAsync(
                synapse.CommandId,
                synapse.AccountId,
                "Different description"));
        var uncertain = await salesforce.ApproveAccountDescriptionAsync(
            synapse.CommandId,
            proposal.Fingerprint);
        var replay = await salesforce.ApproveAccountDescriptionAsync(
            synapse.CommandId,
            proposal.Fingerprint);

        await EmitAsync(new SalesforceMutationVerified(
            proposal.State,
            wrongFingerprintRejected,
            differentArgumentsRejected,
            uncertain.State,
            uncertain == replay));
    }

    private static async Task<bool> RejectsAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
