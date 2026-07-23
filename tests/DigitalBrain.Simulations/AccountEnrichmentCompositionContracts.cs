using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Security;
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
    [Fact(DisplayName = "named Gmail neurons bind token purposes to their complete durable identity")]
    public void NamedGmailAccountsHaveIsolatedTokenPurposes()
    {
        var server = new McpServerDefinition(
            "google.gmail",
            "DigitalBrain Gmail",
            new Uri("https://gmailmcp.googleapis.com/mcp/v1"),
            "DigitalBrain:Google:Gmail",
            ["https://www.googleapis.com/auth/gmail.readonly"]);
        var owner = new OwnerId("named-gmail");
        var first = NeuronId.For<IGmail>(owner, "first@example.com");
        var second = NeuronId.For<IGmail>(owner, "second@example.com");

        Assert.Equal(
            [
                $"mcp/oauth/google.gmail/{first}",
                $"mcp/oauth/google.gmail/{second}",
            ],
            new[]
            {
                McpRuntime.TokenPurpose(server, first.ToString()),
                McpRuntime.TokenPurpose(server, second.ToString()),
            });
    }

    [Fact(DisplayName = "Salesforce approval is a typed human authority fact")]
    public void SalesforceApprovalIsATypedHumanAuthorityFact()
    {
        Assert.True(typeof(Synapse).IsAssignableFrom(typeof(SalesforceMutationApproval)));
        Assert.Equal(
            ["ApprovalId", "ApprovedAt", "Approver", "CommandId", "Fingerprint"],
            typeof(SalesforceMutationApproval)
                .GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
    }

    [Theory(DisplayName = "Salesforce reconciles ambiguity and never repeats the update")]
    [InlineData(null, SalesforceMutationState.OutcomeUncertain)]
    [InlineData("Approved description", SalesforceMutationState.Completed)]
    public async Task SalesforceReconcilesAmbiguityAndNeverRepeatsUpdate(
        string? reconciliationDescription,
        SalesforceMutationState expectedState)
    {
        using var transport = GmailServer();
        transport.FailUpdateCalls = true;
        transport.ReconciliationDescription = reconciliationDescription;
        var cluster = await StartClusterAsync(transport);

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

            var preparedDelivery = await ReadUntilAsync<SalesforceMutationPrepared>(session, verifier);
            var prepared = Assert.IsType<SalesforceMutationPrepared>(preparedDelivery.Synapse);
            var approval = new SalesforceMutationApproval(
                Guid.Parse("ac10f0c8-bc07-445a-a9f8-c82acfb33846"),
                command,
                prepared.Fingerprint,
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session"),
                new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero));
            await session.FireAsync(verifier, approval);

            var delivery = await ReadUntilAsync<SalesforceMutationVerified>(session, verifier);
            var verified = Assert.IsType<SalesforceMutationVerified>(delivery.Synapse);

            Assert.Equal(SalesforceMutationState.AwaitingApproval, verified.ProposedState);
            Assert.True(verified.WrongFingerprintRejected);
            Assert.True(verified.DifferentArgumentsRejected);
            Assert.Equal(expectedState, verified.ApprovedState);
            Assert.True(verified.DifferentEvidenceRejected);
            Assert.True(verified.ReplayReturnedSameReceipt);
            Assert.Single(transport.ToolCalls, call => call.Tool == "update_sobject_record");
            var query = Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
            Assert.Equal(
                "SELECT Id, Description FROM Account WHERE Id = '001000000000042AAA' LIMIT 1",
                query.Arguments.GetProperty("query").GetString());
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
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server);

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
            Assert.DoesNotContain(
                server.ToolCalls,
                call => call.Tool == "update_sobject_record");

            var approval = new SalesforceMutationApproval(
                Guid.Parse("509ab831-d9fd-4be9-ae1a-09e4bda9772f"),
                command.CommandId,
                proposed.Fingerprint,
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session"),
                new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero));

            var forger = NeuronId.For<ApprovalForger>(owner, "forger");
            await session.FireAsync(forger, new ForgeSalesforceApproval(composition, approval));
            await ReadUntilAsync<ApprovalForgeryAttempted>(session, forger);
            var afterForgery = await session.ReadNeuronJournalAsync(
                composition,
                JournalKind.Incoming,
                afterSequence: 0);

            Assert.DoesNotContain(
                afterForgery.Delta,
                delivery => delivery.Synapse is SalesforceMutationApproval);
            Assert.DoesNotContain(
                server.ToolCalls,
                call => call.Tool == "update_sobject_record");

            await session.FireAsync(composition, approval);

            var completed = await ReadUntilAsync<AccountEnriched>(session, composition);
            var outcome = Assert.IsType<AccountEnriched>(completed.Synapse);

            Assert.Equal(command.CommandId, outcome.CommandId);
            Assert.Equal(command.MessageId, outcome.MessageId);
            Assert.Equal(command.AccountId, outcome.AccountId);
            Assert.Equal(
                "Email from priya@northstar.example: Pilot rollout\nWe are ready to start the pilot on Monday.",
                outcome.Description);

            var gmailCall = Assert.Single(server.ToolCalls, call => call.Tool == "get_message");
            Assert.Equal(new Uri("https://gmailmcp.googleapis.com/mcp/v1"), gmailCall.Endpoint);
            Assert.Equal("get_message", gmailCall.Tool);
            Assert.Equal("gmail-message-42", gmailCall.Arguments.GetProperty("messageId").GetString());
            Assert.Equal("FULL_CONTENT", gmailCall.Arguments.GetProperty("messageFormat").GetString());

            var salesforceCall = Assert.Single(
                server.ToolCalls,
                call => call.Tool == "update_sobject_record");
            Assert.Equal(
                new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
                salesforceCall.Endpoint);
            Assert.Equal("update_sobject_record", salesforceCall.Tool);
            Assert.Equal("Account", salesforceCall.Arguments.GetProperty("sobject-name").GetString());
            Assert.Equal(command.AccountId, salesforceCall.Arguments.GetProperty("id").GetString());
            Assert.Equal(
                outcome.Description,
                salesforceCall.Arguments.GetProperty("body").GetProperty("Description").GetString());

            await AssertCapabilityLineageAsync(session, composition, owner);

            await session.FireAsync(composition, approval);
            await ReadUntilCountAsync<AccountEnriched>(session, composition, expectedCount: 2);

            Assert.Single(server.ToolCalls, call => call.Tool == "update_sobject_record");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<InProcessTestCluster> StartClusterAsync(
        FakeMcpHttpServer server)
    {
        var builder = new InProcessTestClusterBuilder(1);

        builder.ConfigureSilo((_, silo) =>
        {
            silo.Configuration["DigitalBrain:Google:Gmail:ClientId"] = "fake-google-client";
            silo.Configuration["DigitalBrain:Google:Gmail:ClientSecret"] = "fake-google-secret";
            silo.Configuration["DigitalBrain:Google:Gmail:RedirectUri"] =
                "http://localhost/fake-google-callback";
            silo.Configuration["DigitalBrain:Salesforce:ClientId"] = "fake-salesforce-client";
            silo.Configuration["DigitalBrain:Salesforce:RedirectUri"] =
                "http://localhost/fake-salesforce-callback";
            silo.Configuration[DurablePayloadProtector.ConfigurationKey] =
                Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
            silo.AddDigitalBrain("account-enrichment");
            GoogleModule.Configure(silo);
            SalesforceModule.Configure(silo);
            silo.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(server));
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

    private static FakeMcpHttpServer GmailServer() =>
        new(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
            subject = "Pilot rollout",
            sender = "priya@northstar.example",
            plaintextBody = "We are ready to start the pilot on Monday.",
        }));

    private static async Task AssertCapabilityLineageAsync(
        ISessionNeuron session,
        NeuronId composition,
        OwnerId owner)
    {
        var outgoing = await session.ReadNeuronJournalAsync(
            composition,
            JournalKind.Outgoing,
            afterSequence: 0);
        var requests = outgoing.Delta
            .Where(delivery => delivery.Synapse is CapabilityRequested)
            .ToArray();
        var gmail = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(IGmail).FullName
            && request.Method == nameof(IGmail.ReadMessageAsync));
        var proposed = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(ISalesforce).FullName
            && request.Method == nameof(ISalesforce.ProposeAccountDescriptionAsync));
        var approved = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(ISalesforce).FullName
            && request.Method == nameof(ISalesforce.ApproveAccountDescriptionAsync));
        var gmailIncoming = await session.ReadNeuronJournalAsync(
            NeuronId.For<IGmail>(owner, "gmail"),
            JournalKind.Incoming,
            afterSequence: 0);
        var salesforceIncoming = await session.ReadNeuronJournalAsync(
            NeuronId.For<ISalesforce>(owner, "salesforce"),
            JournalKind.Incoming,
            afterSequence: 0);

        Assert.Contains(gmailIncoming.Delta, delivery => delivery.SynapseId == gmail.SynapseId);
        Assert.Contains(salesforceIncoming.Delta, delivery => delivery.SynapseId == proposed.SynapseId);
        Assert.Contains(salesforceIncoming.Delta, delivery => delivery.SynapseId == approved.SynapseId);
    }

    private static async Task<SynapseDelivery> ReadUntilAsync<TSynapse>(
        ISessionNeuron session,
        NeuronId neuron)
        where TSynapse : Synapse
    {
        JournalRead? outgoing = null;

        for (var attempt = 0; attempt < 100; attempt++)
        {
            outgoing = await session.ReadNeuronJournalAsync(
                neuron,
                JournalKind.Outgoing,
                afterSequence: 0);
            var delivery = outgoing.Delta.SingleOrDefault(entry => entry.Synapse is TSynapse);

            if (delivery is not null)
            {
                return delivery;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        var incoming = await session.ReadNeuronJournalAsync(
            neuron,
            JournalKind.Incoming,
            afterSequence: 0);
        var incomingSummary = string.Join(
            ", ",
            incoming.Delta.Select(delivery =>
                $"{delivery.Synapse.GetType().Name} from {delivery.Caller}"));
        var outgoingSummary = string.Join(
            ", ",
            outgoing?.Delta.Select(delivery => delivery.Synapse.GetType().Name) ?? []);

        throw new TimeoutException(
            $"Neuron '{neuron}' did not emit {typeof(TSynapse).Name}. "
            + $"Incoming: [{incomingSummary}]. Outgoing: [{outgoingSummary}].");
    }

    private static async Task ReadUntilCountAsync<TSynapse>(
        ISessionNeuron session,
        NeuronId neuron,
        int expectedCount)
        where TSynapse : Synapse
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var journal = await session.ReadNeuronJournalAsync(
                neuron,
                JournalKind.Outgoing,
                afterSequence: 0);

            if (journal.Delta.Count(entry => entry.Synapse is TSynapse) >= expectedCount)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Neuron '{neuron}' did not emit {expectedCount} {typeof(TSynapse).Name} facts.");
    }
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

[GenerateSerializer]
[Alias("db.test.verify-salesforce-mutation")]
internal sealed record VerifySalesforceMutation(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string AccountId,
    [property: Id(2)] string Description) : Synapse;

[GenerateSerializer]
[Alias("db.test.forge-salesforce-approval")]
internal sealed record ForgeSalesforceApproval(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] SalesforceMutationApproval Approval) : Synapse;

[GenerateSerializer]
[Alias("db.test.salesforce-approval-forgery-attempted")]
internal sealed record ApprovalForgeryAttempted : Synapse;

internal sealed class ApprovalForger : Neuron,
    IHandle<ForgeSalesforceApproval>,
    IEmit<ApprovalForgeryAttempted>
{
    public async Task HandleAsync(
        ForgeSalesforceApproval synapse,
        CancellationToken cancellationToken)
    {
        var target = GrainFactory.GetGrain<INeuron>(synapse.Target.ToGrainId());
        await target.DeliverAsync(ForgedDelivery.Create(synapse.Approval, Id));
        await EmitAsync(new ApprovalForgeryAttempted());
    }
}

internal static class ForgedDelivery
{
    internal static SynapseDelivery Create(Synapse synapse, NeuronId caller)
    {
        var constructor = Assert.Single(typeof(SynapseDelivery).GetConstructors(
            BindingFlags.Instance | BindingFlags.NonPublic));

        return Assert.IsType<SynapseDelivery>(constructor.Invoke(
        [
            synapse,
            SynapseId.New(),
            CorrelationId.New(),
            null,
            caller,
            1L,
            new DateTimeOffset(2026, 7, 22, 9, 59, 0, TimeSpan.Zero),
        ]));
    }
}

[GenerateSerializer]
[Alias("db.test.salesforce-mutation-prepared")]
internal sealed record SalesforceMutationPrepared(
    [property: Id(0)] SalesforceMutationState ProposedState,
    [property: Id(1)] string Fingerprint,
    [property: Id(2)] bool DifferentArgumentsRejected) : Synapse;

[GenerateSerializer]
[Alias("db.test.verify-approved-salesforce-mutation")]
internal sealed record VerifyApprovedSalesforceMutation(
    [property: Id(0)] SalesforceMutationApproval Approval) : Synapse;

[GenerateSerializer]
[Alias("db.test.salesforce-mutation-verified")]
internal sealed record SalesforceMutationVerified(
    [property: Id(0)] SalesforceMutationState ProposedState,
    [property: Id(1)] bool WrongFingerprintRejected,
    [property: Id(2)] bool DifferentArgumentsRejected,
    [property: Id(3)] SalesforceMutationState ApprovedState,
    [property: Id(4)] bool ReplayReturnedSameReceipt,
    [property: Id(5)] bool DifferentEvidenceRejected) : Synapse;

internal sealed class SalesforceMutationVerifier : Neuron,
    IHandle<VerifySalesforceMutation>,
    IHandle<SalesforceMutationApproval>,
    IHandle<VerifyApprovedSalesforceMutation>,
    IEmit<SalesforceMutationPrepared>,
    IEmit<SalesforceMutationVerified>
{
    private VerifySalesforceMutation? _request;
    private SalesforceAccountDescriptionMutation? _proposal;
    private bool _differentArgumentsRejected;

    public async Task HandleAsync(
        VerifySalesforceMutation synapse,
        CancellationToken cancellationToken)
    {
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var proposal = await salesforce.ProposeAccountDescriptionAsync(
            synapse.CommandId,
            Id,
            synapse.AccountId,
            synapse.Description,
            cancellationToken);
        var differentArgumentsRejected = await RejectsAsync(
            () => salesforce.ProposeAccountDescriptionAsync(
                synapse.CommandId,
                Id,
                synapse.AccountId,
                "Different description",
                cancellationToken));

        _request = synapse;
        _proposal = proposal;
        _differentArgumentsRejected = differentArgumentsRejected;
        await EmitAsync(new SalesforceMutationPrepared(
            proposal.State,
            proposal.Fingerprint,
            differentArgumentsRejected));
    }

    public async Task HandleAsync(
        SalesforceMutationApproval synapse,
        CancellationToken cancellationToken)
    {
        await SendAsync(Id, new VerifyApprovedSalesforceMutation(synapse));
    }

    public async Task HandleAsync(
        VerifyApprovedSalesforceMutation synapse,
        CancellationToken cancellationToken)
    {
        _ = _request
            ?? throw new InvalidOperationException("No Salesforce verification is awaiting approval.");
        var proposal = _proposal
            ?? throw new InvalidOperationException("No Salesforce proposal is awaiting approval.");
        var approval = synapse.Approval;
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        var incoming = await ReadJournalAsync(JournalKind.Incoming, afterSequence: 0);
        var evidence = incoming.Delta.Single(delivery =>
            delivery.Caller == approval.Approver
            && delivery.Synapse is SalesforceMutationApproval recorded
            && recorded == approval);
        var wrongFingerprintRejected = await RejectsAsync(
            () => salesforce.ApproveAccountDescriptionAsync(
                approval with { Fingerprint = "WRONG-FINGERPRINT" },
                evidence,
                cancellationToken));
        var uncertain = await salesforce.ApproveAccountDescriptionAsync(
            approval,
            evidence,
            cancellationToken);
        var differentEvidenceRejected = await RejectsAuthorizationAsync(
            () => salesforce.ApproveAccountDescriptionAsync(
                approval,
                ForgedDelivery.Create(approval, approval.Approver),
                cancellationToken));
        var replay = await salesforce.ApproveAccountDescriptionAsync(
            approval,
            evidence,
            cancellationToken);

        await EmitAsync(new SalesforceMutationVerified(
            proposal.State,
            wrongFingerprintRejected,
            _differentArgumentsRejected,
            uncertain.State,
            uncertain == replay,
            differentEvidenceRejected));
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

    private static async Task<bool> RejectsAuthorizationAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (NeuronAuthorizationException)
        {
            return true;
        }
    }
}
