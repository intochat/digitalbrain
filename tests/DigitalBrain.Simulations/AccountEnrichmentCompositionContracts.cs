using System.Reflection;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.AccountEnrichment;
using DigitalBrain.Google;
using DigitalBrain.Integrations.Mcp;
using DigitalBrain.Kernel;
using DigitalBrain.Salesforce;
using DigitalBrain.Security;
using DigitalBrain.Testing;
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
    [Fact(DisplayName = "account enrichment appends the named Gmail account to its public wire contract")]
    public void AccountEnrichmentAppendsTheNamedGmailAccountToItsPublicWireContract()
    {
        var members = typeof(EnrichAccountFromEmail)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (
                property.Name,
                Id: property.GetCustomAttribute<IdAttribute>()?.Id))
            .Where(member => member.Id.HasValue)
            .OrderBy(member => member.Id)
            .Select(member => (member.Name, member.Id!.Value))
            .ToArray();

        Assert.Equal(
            [
                ("CommandId", 0u),
                ("MessageId", 1u),
                ("AccountId", 2u),
                ("GmailAccount", 3u),
            ],
            members);
    }

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

    [Fact(DisplayName = "account enrichment rejects empty provider identifiers before provider access")]
    public async Task AccountEnrichmentRejectsEmptyProviderIdentifiersBeforeProviderAccess()
    {
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server);

        try
        {
            var owner = new OwnerId("account-enrichment-empty-identifiers");
            var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
            var caller = new NeuronId(ISessionNeuron.GrainTypeName, owner, "session");
            var session = cluster.Client.GetGrain<ISessionNeuron>(caller.ToGrainId());
            var probe = NeuronId.For<AccountEnrichmentDeliveryProbe>(owner, "delivery-probe");
            var command = EnrichmentRequest(
                CommandId.New(),
                "account-enrichment@example.com");
            var invalid = new[]
            {
                command with { GmailAccount = " " },
                command with { MessageId = " " },
                command with { AccountId = " " },
            };

            foreach (var candidate in invalid)
            {
                var result = await ProbeDeliveryAsync(
                    session,
                    probe,
                    composition,
                    candidate,
                    caller);
                Assert.Equal("ArgumentException", result.Failure);
            }

            Assert.Empty(server.Requests);
            Assert.Empty(server.ToolCalls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Theory(DisplayName = "invalid Gmail identities do not poison an enrichment command")]
    [InlineData("account/enrichment@example.com")]
    [InlineData("account enrichment@example.com")]
    public async Task InvalidGmailIdentitiesDoNotPoisonAnEnrichmentCommand(
        string invalidGmailAccount)
    {
        var journals = new AIWorkerJournalStorageProvider();
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server, journals: journals);
        var owner = new OwnerId("account-enrichment-invalid-gmail-identity");
        var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
        var process = composition.ToGrainId();

        try
        {
            var caller = new NeuronId(ISessionNeuron.GrainTypeName, owner, "session");
            var session = cluster.Client.GetGrain<ISessionNeuron>(caller.ToGrainId());
            var probe = NeuronId.For<AccountEnrichmentDeliveryProbe>(owner, "delivery-probe");
            var command = EnrichmentRequest(CommandId.New(), invalidGmailAccount);

            Assert.Throws<ArgumentException>(
                () => NeuronId.For<IGmail>(owner, invalidGmailAccount));

            var rejected = await ProbeDeliveryAsync(
                session,
                probe,
                composition,
                command,
                caller);
            var outgoingAfterRejection = await session.ReadNeuronJournal(
                composition,
                JournalKind.Outgoing,
                afterSequence: 0);

            Assert.Equal("ArgumentException", rejected.Failure);
            Assert.Equal(0, journals.CompletedWrites(process));
            Assert.DoesNotContain(
                outgoingAfterRejection.Delta,
                delivery => delivery.Synapse.GetType().Name
                    == "ContinueAccountEnrichment");
            Assert.Empty(server.Requests);
            Assert.Empty(server.ToolCalls);

            const string validGmailAccount = "account-enrichment@example.com";
            await session.Fire(
                composition,
                command with { GmailAccount = validGmailAccount });
            await ReadUntilAsync<AccountEnrichmentProposed>(session, composition);
            var namedGmailIncoming = await session.ReadNeuronJournal(
                NeuronId.For<IGmail>(owner, validGmailAccount),
                JournalKind.Incoming,
                afterSequence: 0);

            Assert.Single(
                namedGmailIncoming.Delta,
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Method == nameof(IGmail.ReadMessage));
            Assert.Equal(
                1,
                await CountOutgoingAsync<AccountEnrichmentProposed>(
                    session,
                    composition));
            Assert.Equal(
                1,
                await CountOutgoingAsync(
                    session,
                    composition,
                    "ContinueAccountEnrichment"));
            Assert.Equal(
                1,
                server.ToolCalls.Count(call => call.Tool == "get_message"));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "account enrichment resolves each exact named Gmail neuron")]
    public async Task AccountEnrichmentResolvesEachExactNamedGmailNeuron()
    {
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server);

        try
        {
            var owner = new OwnerId("account-enrichment-named-gmail");
            var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
            var accounts = new[]
            {
                "first@example.com",
                "second@example.com",
            };

            foreach (var account in accounts)
            {
                await session.Fire(
                    composition,
                    EnrichmentRequest(CommandId.New(), account));
            }

            await ReadUntilCountAsync<AccountEnrichmentProposed>(
                session,
                composition,
                expectedCount: accounts.Length);
            var outgoing = await session.ReadNeuronJournal(
                composition,
                JournalKind.Outgoing,
                afterSequence: 0);
            var targets = outgoing.Delta
                .Select(delivery => delivery.Synapse)
                .OfType<CapabilityRequested>()
                .Where(request => request.Contract == typeof(IGmail).FullName)
                .Select(request => request.Target)
                .ToArray();

            Assert.Equal(
                accounts
                    .Select(account => NeuronId.For<IGmail>(owner, account))
                    .OrderBy(identity => identity.ToString(), StringComparer.Ordinal),
                targets.OrderBy(identity => identity.ToString(), StringComparer.Ordinal));

            foreach (var account in accounts)
            {
                var incoming = await session.ReadNeuronJournal(
                    NeuronId.For<IGmail>(owner, account),
                    JournalKind.Incoming,
                    afterSequence: 0);
                Assert.Single(
                    incoming.Delta,
                    delivery => delivery.Synapse is CapabilityRequested request
                        && request.Method == nameof(IGmail.ReadMessage));
            }
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Theory(DisplayName = "failed enrichment fence or proposal writes restore state and retry cleanly")]
    [InlineData(0, 1)]
    [InlineData(5, 2)]
    public async Task FailedEnrichmentWritesRestoreStateAndRetryCleanly(
        int completedWritesBeforeFailure,
        int expectedGmailReads)
    {
        var journals = new AIWorkerJournalStorageProvider();
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server, journals: journals);
        var owner = new OwnerId($"account-enrichment-write-{completedWritesBeforeFailure}");
        var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
        var process = composition.ToGrainId();

        try
        {
            var caller = new NeuronId(ISessionNeuron.GrainTypeName, owner, "session");
            var session = cluster.Client.GetGrain<ISessionNeuron>(caller.ToGrainId());
            var command = EnrichmentRequest(
                CommandId.New(),
                "account-enrichment@example.com");
            journals.FailWriteAfter(
                process,
                completedWritesBeforeFailure,
                "Expected account-enrichment write failure.");

            await session.Fire(composition, command);

            if (completedWritesBeforeFailure == 5)
            {
                await WaitUntilAsync(
                    () => journals.FiredFailures(process) == 1,
                    "The injected proposal commit failure did not fire.");
                await WaitUntilAsync(
                    () => journals.CompletedWrites(process) == 6,
                    "The failed proposal turn did not durably preserve its pending self-delivery.");
                var outgoing = await session.ReadNeuronJournal(
                    composition,
                    JournalKind.Outgoing,
                    afterSequence: 0);
                var continuation = Assert.Single(
                    outgoing.Delta,
                    delivery => delivery.Synapse.GetType().Name
                        == "ContinueAccountEnrichment");

                Assert.DoesNotContain(
                    outgoing.Delta,
                    delivery => delivery.Synapse is AccountEnrichmentProposed);
                Assert.Equal(
                    1,
                    server.ToolCalls.Count(call => call.Tool == "get_message"));

                await session.Fire(composition, continuation.Synapse);
            }

            await ReadUntilAsync<AccountEnrichmentProposed>(session, composition);

            Assert.Equal(1, journals.FiredFailures(process));
            Assert.Equal(
                expectedGmailReads,
                server.ToolCalls.Count(call => call.Tool == "get_message"));
            Assert.Equal(
                1,
                await CountOutgoingAsync<AccountEnrichmentProposed>(session, composition));
        }
        finally
        {
            journals.ClearFailure(process);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "account enrichment commits its request fence before any provider call")]
    public async Task AccountEnrichmentCommitsItsRequestFenceBeforeAnyProviderCall()
    {
        var journals = new AIWorkerJournalStorageProvider();
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server, journals: journals);
        var owner = new OwnerId("account-enrichment-fence-order");
        var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
        var gate = journals.BlockNextWrite(composition.ToGrainId());

        try
        {
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());

            await session.Fire(
                composition,
                EnrichmentRequest(CommandId.New(), "account-enrichment@example.com"));
            await gate.Entered.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Empty(server.Requests);
            Assert.Empty(server.ToolCalls);

            gate.Release();
            await ReadUntilAsync<AccountEnrichmentProposed>(session, composition);
            var outgoing = await session.ReadNeuronJournal(
                composition,
                JournalKind.Outgoing,
                afterSequence: 0);

            Assert.Equal(
                "ContinueAccountEnrichment",
                outgoing.Delta[0].Synapse.GetType().Name);
            Assert.Contains(
                outgoing.Delta.Skip(1),
                delivery => delivery.Synapse is CapabilityRequested request
                    && request.Contract == typeof(IGmail).FullName);
        }
        finally
        {
            gate.Release();
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "account enrichment rejects changed replay before providers and exact replay is inert")]
    public async Task AccountEnrichmentRejectsChangedReplayBeforeProvidersAndExactReplayIsInert()
    {
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server);

        try
        {
            var owner = new OwnerId("account-enrichment-replay");
            var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
            var caller = new NeuronId(ISessionNeuron.GrainTypeName, owner, "session");
            var session = cluster.Client.GetGrain<ISessionNeuron>(caller.ToGrainId());
            var probe = NeuronId.For<AccountEnrichmentDeliveryProbe>(owner, "delivery-probe");
            var command = EnrichmentRequest(
                CommandId.New(),
                "account-enrichment@example.com");

            await session.Fire(composition, command);
            await ReadUntilAsync<AccountEnrichmentProposed>(session, composition);
            var requestsBeforeReplay = server.Requests.Count;
            var callsBeforeReplay = server.ToolCalls.Count;

            var replay = await ProbeDeliveryAsync(
                session,
                probe,
                composition,
                command,
                caller);

            Assert.Null(replay.Failure);
            Assert.Equal(requestsBeforeReplay, server.Requests.Count);
            Assert.Equal(callsBeforeReplay, server.ToolCalls.Count);
            Assert.Equal(
                1,
                await CountOutgoingAsync<AccountEnrichmentProposed>(session, composition));
            Assert.Equal(
                1,
                await CountOutgoingAsync(
                    session,
                    composition,
                    "ContinueAccountEnrichment"));

            var changed = new[]
            {
                command with { GmailAccount = "different@example.com" },
                command with { MessageId = "different-message" },
                command with { AccountId = "001000000000043AAA" },
            };

            foreach (var candidate in changed)
            {
                var result = await ProbeDeliveryAsync(
                    session,
                    probe,
                    composition,
                    candidate,
                    caller);
                Assert.Equal("InvalidOperationException", result.Failure);
                Assert.Equal(requestsBeforeReplay, server.Requests.Count);
                Assert.Equal(callsBeforeReplay, server.ToolCalls.Count);
            }
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
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

    [Fact(DisplayName = "Salesforce rejects every local tool-contract drift before mutation")]
    public async Task SalesforceRejectsEveryLocalToolContractDriftBeforeMutation()
    {
        using var transport = GmailServer();
        var cluster = await StartClusterAsync(transport);
        var faults = Enum.GetValues<SalesforceToolFault>()
            .Where(fault => fault is not (
                SalesforceToolFault.None
                or SalesforceToolFault.UpdateAnnotations
                or SalesforceToolFault.UpdateDestructive))
            .ToArray();

        try
        {
            foreach (var fault in faults)
            {
                transport.SalesforceFault = fault;
                var before = transport.Requests.Count;
                var result = await ExerciseSalesforceAsync(
                    cluster,
                    $"salesforce-contract-{fault}",
                    SalesforceProbeMode.Normal);

                Assert.Null(result.Prepared.Failure);
                Assert.NotNull(result.Verified.Failure);
                Assert.Equal(
                    [new McpRequest(before / 2 + 1, "initialize"), new McpRequest(before / 2 + 1, "tools/list")],
                    transport.Requests.Skip(before));
                Assert.DoesNotContain(
                    transport.ToolCalls,
                    call => call.Connection == before / 2 + 1);
            }
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Theory(DisplayName = "Salesforce update admission requires destructive mutation authority")]
    [InlineData((int)SalesforceToolFault.None, true)]
    [InlineData((int)SalesforceToolFault.UpdateDestructive, false)]
    [InlineData((int)SalesforceToolFault.UpdateAnnotations, false)]
    public async Task SalesforceUpdateAdmissionRequiresDestructiveMutationAuthority(
        int faultValue,
        bool admitted)
    {
        var fault = (SalesforceToolFault)faultValue;
        using var transport = GmailServer();
        transport.SalesforceFault = fault;
        var cluster = await StartClusterAsync(transport);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                $"salesforce-destructive-authority-{fault}",
                SalesforceProbeMode.Normal);

            Assert.Null(result.Prepared.Failure);
            Assert.Equal(admitted, result.Verified.Failure is null);
            Assert.Equal(
                admitted ? 1 : 0,
                transport.ToolCalls.Count(call => call.Tool == "updateSobjectRecord"));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce cancellation before its durable fence makes no provider request")]
    public async Task SalesforceCancellationBeforeFenceMakesNoProviderRequest()
    {
        using var transport = GmailServer();
        var cluster = await StartClusterAsync(transport);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                "salesforce-cancel-before-fence",
                SalesforceProbeMode.CancelBeforeFence);

            Assert.Null(result.Prepared.Failure);
            Assert.Equal("OperationCanceledException", result.Verified.Failure);
            Assert.Empty(transport.Requests);
            Assert.Empty(transport.ToolCalls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce re-lists on a fresh mutation connection and fails closed on drift")]
    public async Task SalesforceRelistsOnFreshMutationConnectionAndFailsClosedOnDrift()
    {
        using var transport = GmailServer();
        transport.MutationConnectionFault = SalesforceToolFault.UpdateOutput;
        var cluster = await StartClusterAsync(transport);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                "salesforce-fresh-mutation-drift",
                SalesforceProbeMode.Normal);

            Assert.Null(result.Verified.Failure);
            Assert.Equal(SalesforceMutationState.OutcomeUncertain, result.Verified.ApprovedState);
            Assert.DoesNotContain(transport.ToolCalls, call => call.Tool == "updateSobjectRecord");
            Assert.Equal(3, transport.Requests.Count(request => request.Method == "initialize"));
            Assert.Equal(3, transport.Requests.Count(request => request.Method == "tools/list"));
            Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce caller cancellation after the fence still runs one bounded fresh reconciliation")]
    public async Task SalesforceCallerCancellationAfterFenceStillRunsOneBoundedFreshReconciliation()
    {
        var cancellation = new SalesforceCancellationProbe();
        using var transport = GmailServer(cancellation);
        transport.FailUpdateCalls = true;
        transport.ReconciliationDescription = "Approved description";
        var cluster = await StartClusterAsync(transport, cancellation);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                "salesforce-cancel-after-fence",
                SalesforceProbeMode.CancelAfterFence);

            Assert.Equal("OperationCanceledException", result.Verified.Failure);
            Assert.Equal(SalesforceMutationState.Completed, result.Verified.ApprovedState);
            var update = Assert.Single(
                transport.ToolCalls,
                call => call.Tool == "updateSobjectRecord");
            var query = Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
            Assert.Equal(2, update.Connection);
            Assert.Equal(3, query.Connection);
            Assert.True(transport.ReconciliationTokenCanBeCanceled);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce bounded reconciliation cancellation and query drift become OutcomeUncertain")]
    public async Task SalesforceBoundedReconciliationFailureBecomesOutcomeUncertain()
    {
        using var transport = GmailServer();
        transport.FailUpdateCalls = true;
        transport.BlockReconciliationUntilCancellation = true;
        var cluster = await StartClusterAsync(
            transport,
            reconciliationTimeout: TimeSpan.FromMilliseconds(100));

        try
        {
            var result = await ExerciseSalesforceAsync(
                    cluster,
                    "salesforce-reconciliation-timeout",
                    SalesforceProbeMode.Normal)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Null(result.Verified.Failure);
            Assert.Equal(SalesforceMutationState.OutcomeUncertain, result.Verified.ApprovedState);
            Assert.True(transport.ReconciliationTokenCanBeCanceled);
            Assert.True(transport.ReconciliationCancellationObserved);
            Assert.Single(transport.ToolCalls, call => call.Tool == "updateSobjectRecord");
            Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce reconciliation schema drift becomes OutcomeUncertain without a query call")]
    public async Task SalesforceReconciliationSchemaDriftBecomesOutcomeUncertain()
    {
        using var transport = GmailServer();
        transport.FailUpdateCalls = true;
        transport.ReconciliationConnectionFault = SalesforceToolFault.QueryOutput;
        transport.ReconciliationDescription = "Approved description";
        var cluster = await StartClusterAsync(transport);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                "salesforce-reconciliation-drift",
                SalesforceProbeMode.Normal);

            Assert.Null(result.Verified.Failure);
            Assert.Equal(SalesforceMutationState.OutcomeUncertain, result.Verified.ApprovedState);
            Assert.Single(transport.ToolCalls, call => call.Tool == "updateSobjectRecord");
            Assert.DoesNotContain(transport.ToolCalls, call => call.Tool == "soqlQuery");
            Assert.Equal(3, transport.Requests.Count(request => request.Method == "tools/list"));
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Theory(DisplayName = "Salesforce error and malformed update results cannot become success")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SalesforceErrorAndMalformedUpdateResultsCannotBecomeSuccess(
        bool isError,
        bool omitStructuredContent)
    {
        using var transport = GmailServer();
        transport.ToolResultIsError = isError;
        transport.OmitStructuredContent = omitStructuredContent;
        var cluster = await StartClusterAsync(transport);

        try
        {
            var result = await ExerciseSalesforceAsync(
                cluster,
                $"salesforce-bad-result-{isError}-{omitStructuredContent}",
                SalesforceProbeMode.Normal);

            Assert.Null(result.Verified.Failure);
            Assert.Equal(SalesforceMutationState.OutcomeUncertain, result.Verified.ApprovedState);
            Assert.Single(transport.ToolCalls, call => call.Tool == "updateSobjectRecord");
            Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce failed fence save rolls back before approval retry")]
    public async Task SalesforceFailedFenceSaveRollsBackBeforeApprovalRetry()
    {
        var journals = new AIWorkerJournalStorageProvider();
        using var transport = GmailServer();
        var cluster = await StartClusterAsync(transport, journals: journals);
        var owner = new OwnerId("salesforce-fence-write-rollback");
        var salesforce = NeuronId.For<ISalesforce>(owner, "salesforce").ToGrainId();

        try
        {
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
            var verifier = NeuronId.For<SalesforceMutationVerifier>(owner, "verifier");
            var command = CommandId.New();
            await session.Fire(verifier, new VerifySalesforceMutation(
                command,
                "001000000000042AAA",
                "Approved description",
                SalesforceProbeMode.RetryAfterFailure));
            var preparedDelivery = await ReadUntilAsync<SalesforceMutationPrepared>(session, verifier);
            var prepared = Assert.IsType<SalesforceMutationPrepared>(preparedDelivery.Synapse);
            Assert.Empty(transport.Requests);
            Assert.Empty(transport.ToolCalls);
            var writesAfterProposal = journals.CompletedWrites(salesforce);
            transport.DurableWrites = () => journals.CompletedWrites(salesforce);
            journals.FailWriteAfter(
                salesforce,
                completedWritesBeforeFailure: 0,
                "Expected Salesforce invoking-fence write failure.");
            var approval = Approval(owner, command, prepared.Fingerprint);

            await session.Fire(verifier, approval);
            var verifiedDelivery = await ReadUntilAsync<SalesforceMutationVerified>(session, verifier);
            var verified = Assert.IsType<SalesforceMutationVerified>(verifiedDelivery.Synapse);

            Assert.Equal(1, journals.FiredFailures(salesforce));
            Assert.Equal(SalesforceMutationState.Completed, verified.ApprovedState);
            Assert.Single(transport.ToolCalls, call => call.Tool == "updateSobjectRecord");
            Assert.DoesNotContain(transport.ToolCalls, call => call.Tool == "soqlQuery");
            Assert.True(transport.DurableWritesAtUpdate > writesAfterProposal);
        }
        finally
        {
            journals.ClearFailure(salesforce);
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    [Fact(DisplayName = "Salesforce rejects invalid Account IDs before any provider request")]
    public async Task SalesforceRejectsInvalidAccountIdsBeforeAnyProviderRequest()
    {
        using var transport = GmailServer();
        var cluster = await StartClusterAsync(transport);

        try
        {
            var owner = new OwnerId("salesforce-invalid-account");
            var verifier = NeuronId.For<SalesforceMutationVerifier>(owner, "verifier");
            var session = cluster.Client.GetGrain<ISessionNeuron>(
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
            await session.Fire(verifier, new VerifySalesforceMutation(
                CommandId.New(),
                "001' OR Name != '",
                "Approved description"));
            var preparedDelivery = await ReadUntilAsync<SalesforceMutationPrepared>(session, verifier);
            var prepared = Assert.IsType<SalesforceMutationPrepared>(preparedDelivery.Synapse);

            Assert.Equal("ArgumentException", prepared.Failure);
            Assert.Empty(transport.Requests);
            Assert.Empty(transport.ToolCalls);
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
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
            await session.Fire(verifier, new VerifySalesforceMutation(
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
            await session.Fire(verifier, approval);

            var delivery = await ReadUntilAsync<SalesforceMutationVerified>(session, verifier);
            var verified = Assert.IsType<SalesforceMutationVerified>(delivery.Synapse);

            Assert.Equal(SalesforceMutationState.AwaitingApproval, verified.ProposedState);
            Assert.True(verified.WrongFingerprintRejected);
            Assert.True(verified.DifferentArgumentsRejected);
            Assert.Equal(expectedState, verified.ApprovedState);
            Assert.True(verified.DifferentEvidenceRejected);
            Assert.True(verified.ReplayReturnedSameReceipt);
            Assert.Null(verified.Failure);
            var update = Assert.Single(
                transport.ToolCalls,
                call => call.Tool == "updateSobjectRecord");
            var query = Assert.Single(transport.ToolCalls, call => call.Tool == "soqlQuery");
            Assert.Equal(2, update.Connection);
            Assert.Equal(3, query.Connection);
            Assert.Equal(3, transport.Requests.Count(request => request.Method == "tools/list"));
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

    [Fact(DisplayName = "failed account enrichment completion writes restore state and emit once")]
    public async Task FailedAccountEnrichmentCompletionWritesRestoreStateAndEmitOnce()
    {
        var journals = new AIWorkerJournalStorageProvider();
        using var server = GmailServer();
        var cluster = await StartClusterAsync(server, journals: journals);
        var owner = new OwnerId("account-enrichment-completion-write");
        var composition = NeuronId.For<AccountEnrichmentProcess>(owner, "enrich-account");
        var processId = composition.ToGrainId();

        try
        {
            var caller = new NeuronId(ISessionNeuron.GrainTypeName, owner, "session");
            var session = cluster.Client.GetGrain<ISessionNeuron>(caller.ToGrainId());
            var probe = NeuronId.For<AccountEnrichmentDeliveryProbe>(owner, "delivery-probe");
            var command = EnrichmentRequest(
                CommandId.New(),
                "account-enrichment@example.com");
            await session.Fire(composition, command);
            var proposedDelivery = await ReadUntilAsync<AccountEnrichmentProposed>(
                session,
                composition);
            var proposed = Assert.IsType<AccountEnrichmentProposed>(proposedDelivery.Synapse);
            var approval = Approval(owner, command.CommandId, proposed.Fingerprint);
            journals.FailWriteAfter(
                processId,
                completedWritesBeforeFailure: 1,
                "Expected account-enrichment completion write failure.");

            var approvalResult = await ProbeDeliveryAsync(
                session,
                probe,
                composition,
                approval,
                caller);
            Assert.Null(approvalResult.Failure);
            await ReadUntilAsync<AccountEnriched>(session, composition);

            Assert.Equal(1, journals.FiredFailures(processId));
            Assert.Equal(
                1,
                await CountOutgoingAsync<AccountEnriched>(session, composition));
            Assert.Single(
                server.ToolCalls,
                call => call.Tool == "updateSobjectRecord");
        }
        finally
        {
            journals.ClearFailure(processId);
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
                "001000000000042AAA",
                "account-enrichment@example.com");

            await session.Fire(composition, command);

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
                call => call.Tool == "updateSobjectRecord");

            var approval = new SalesforceMutationApproval(
                Guid.Parse("509ab831-d9fd-4be9-ae1a-09e4bda9772f"),
                command.CommandId,
                proposed.Fingerprint,
                new NeuronId(ISessionNeuron.GrainTypeName, owner, "session"),
                new DateTimeOffset(2026, 7, 22, 10, 0, 0, TimeSpan.Zero));

            var forger = NeuronId.For<ApprovalForger>(owner, "forger");
            await session.Fire(forger, new ForgeSalesforceApproval(composition, approval));
            await ReadUntilAsync<ApprovalForgeryAttempted>(session, forger);
            var afterForgery = await session.ReadNeuronJournal(
                composition,
                JournalKind.Incoming,
                afterSequence: 0);

            Assert.DoesNotContain(
                afterForgery.Delta,
                delivery => delivery.Synapse is SalesforceMutationApproval);
            Assert.DoesNotContain(
                server.ToolCalls,
                call => call.Tool == "updateSobjectRecord");

            await session.Fire(composition, approval);

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
                call => call.Tool == "updateSobjectRecord");
            Assert.Equal(
                new Uri("https://api.salesforce.com/platform/mcp/v1/platform/sobject-mutations"),
                salesforceCall.Endpoint);
            Assert.Equal("updateSobjectRecord", salesforceCall.Tool);
            Assert.Equal("Account", salesforceCall.Arguments.GetProperty("sobject-name").GetString());
            Assert.Equal(command.AccountId, salesforceCall.Arguments.GetProperty("id").GetString());
            Assert.Equal(
                outcome.Description,
                salesforceCall.Arguments.GetProperty("body").GetProperty("Description").GetString());

            await AssertCapabilityLineageAsync(
                session,
                composition,
                owner,
                command.GmailAccount);
            var incoming = await session.ReadNeuronJournal(
                composition,
                JournalKind.Incoming,
                afterSequence: 0);
            Assert.Single(
                incoming.Delta,
                delivery => delivery.Caller == approval.Approver
                    && delivery.Synapse is SalesforceMutationApproval recorded
                    && recorded == approval);
            var providerRequests = server.Requests.Count;
            var providerCalls = server.ToolCalls.Count;
            var probe = NeuronId.For<AccountEnrichmentDeliveryProbe>(owner, "delivery-probe");

            var requestReplay = await ProbeDeliveryAsync(
                session,
                probe,
                composition,
                command,
                approval.Approver);
            var approvalReplay = await ProbeDeliveryAsync(
                session,
                probe,
                composition,
                approval,
                approval.Approver);

            Assert.Null(requestReplay.Failure);
            Assert.Null(approvalReplay.Failure);
            Assert.Equal(providerRequests, server.Requests.Count);
            Assert.Equal(providerCalls, server.ToolCalls.Count);
            Assert.Equal(
                1,
                await CountOutgoingAsync<AccountEnriched>(session, composition));
            Assert.Equal(
                1,
                await CountOutgoingAsync(
                    session,
                    composition,
                    "ContinueAccountEnrichment"));
            Assert.Equal(
                1,
                await CountOutgoingAsync(
                    session,
                    composition,
                    "ExecuteApprovedAccountEnrichment"));
            Assert.Single(server.ToolCalls, call => call.Tool == "updateSobjectRecord");
        }
        finally
        {
            await cluster.StopAllSilosAsync();
            await cluster.DisposeAsync();
        }
    }

    private static async Task<(
        SalesforceMutationPrepared Prepared,
        SalesforceMutationVerified Verified)> ExerciseSalesforceAsync(
        InProcessTestCluster cluster,
        string ownerName,
        SalesforceProbeMode mode)
    {
        var owner = new OwnerId(ownerName);
        var verifier = NeuronId.For<SalesforceMutationVerifier>(owner, "verifier");
        var session = cluster.Client.GetGrain<ISessionNeuron>(
            new NeuronId(ISessionNeuron.GrainTypeName, owner, "session").ToGrainId());
        var command = CommandId.New();
        await session.Fire(verifier, new VerifySalesforceMutation(
            command,
            "001000000000042AAA",
            "Approved description",
            mode));
        var preparedDelivery = await ReadUntilAsync<SalesforceMutationPrepared>(session, verifier);
        var prepared = Assert.IsType<SalesforceMutationPrepared>(preparedDelivery.Synapse);
        Assert.Null(prepared.Failure);

        await session.Fire(
            verifier,
            Approval(owner, command, prepared.Fingerprint));
        var verifiedDelivery = await ReadUntilAsync<SalesforceMutationVerified>(session, verifier);
        var verified = Assert.IsType<SalesforceMutationVerified>(verifiedDelivery.Synapse);
        return (prepared, verified);
    }

    private static async Task<AccountEnrichmentDeliveryProbed> ProbeDeliveryAsync(
        ISessionNeuron session,
        NeuronId probe,
        NeuronId target,
        Synapse synapse,
        NeuronId caller)
    {
        var probeId = Guid.NewGuid();
        await session.Fire(
            probe,
            new ProbeAccountEnrichmentDelivery(
                probeId,
                target,
                synapse,
                caller));

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var outgoing = await session.ReadNeuronJournal(
                probe,
                JournalKind.Outgoing,
                afterSequence: 0);
            var result = outgoing.Delta
                .Select(delivery => delivery.Synapse)
                .OfType<AccountEnrichmentDeliveryProbed>()
                .FirstOrDefault(candidate => candidate.ProbeId == probeId);

            if (result is not null)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(
            $"Delivery probe '{probeId}' did not report a result for '{target}'.");
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition,
        string timeoutMessage)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static SalesforceMutationApproval Approval(
        OwnerId owner,
        CommandId command,
        string fingerprint) =>
        new(
            Guid.NewGuid(),
            command,
            fingerprint,
            new NeuronId(ISessionNeuron.GrainTypeName, owner, "session"),
            new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero));

    private static EnrichAccountFromEmail EnrichmentRequest(
        CommandId command,
        string gmailAccount) =>
        new(
            command,
            "gmail-message-42",
            "001000000000042AAA",
            gmailAccount);

    private static async Task<InProcessTestCluster> StartClusterAsync(
        FakeMcpHttpServer server,
        SalesforceCancellationProbe? cancellation = null,
        IJournalStorageProvider? journals = null,
        TimeSpan? reconciliationTimeout = null)
    {
        var builder = new InProcessTestClusterBuilder(1);
        cancellation ??= new SalesforceCancellationProbe();

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
            ((ICompiledModule)new GoogleModule()).Activate(silo);
            ((ICompiledModule)new SalesforceModule()).Activate(silo);
            if (reconciliationTimeout is { } timeout)
            {
                silo.Services.AddSingleton(new SalesforceRuntimeOptions(timeout));
            }

            silo.Services.AddSingleton<IHttpClientFactory>(new FakeHttpClientFactory(server));
            silo.Services.AddSingleton(cancellation);
            silo.UseInMemoryReminderService();
            silo.Services.AddSingleton(
                journals ?? new VolatileJournalStorageProvider());
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

    private static FakeMcpHttpServer GmailServer(
        SalesforceCancellationProbe? cancellation = null) =>
        new(JsonSerializer.SerializeToElement(new
        {
            id = "gmail-message-42",
            subject = "Pilot rollout",
            sender = "priya@northstar.example",
            plaintextBody = "We are ready to start the pilot on Monday.",
        }), cancellation);

    private static async Task AssertCapabilityLineageAsync(
        ISessionNeuron session,
        NeuronId composition,
        OwnerId owner,
        string gmailAccount)
    {
        var outgoing = await session.ReadNeuronJournal(
            composition,
            JournalKind.Outgoing,
            afterSequence: 0);
        var requests = outgoing.Delta
            .Where(delivery => delivery.Synapse is CapabilityRequested)
            .ToArray();
        var gmail = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(IGmail).FullName
            && request.Method == nameof(IGmail.ReadMessage));
        var proposed = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(ISalesforce).FullName
            && request.Method == nameof(ISalesforce.ProposeAccountDescription));
        var approved = Assert.Single(requests, delivery =>
            delivery.Synapse is CapabilityRequested request
            && request.Contract == typeof(ISalesforce).FullName
            && request.Method == nameof(ISalesforce.ApproveAccountDescription));
        var gmailIncoming = await session.ReadNeuronJournal(
            NeuronId.For<IGmail>(owner, gmailAccount),
            JournalKind.Incoming,
            afterSequence: 0);
        var salesforceIncoming = await session.ReadNeuronJournal(
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
            outgoing = await session.ReadNeuronJournal(
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

        var incoming = await session.ReadNeuronJournal(
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
            var journal = await session.ReadNeuronJournal(
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

    private static async Task<int> CountOutgoingAsync<TSynapse>(
        ISessionNeuron session,
        NeuronId neuron)
        where TSynapse : Synapse
    {
        var outgoing = await session.ReadNeuronJournal(
            neuron,
            JournalKind.Outgoing,
            afterSequence: 0);
        return outgoing.Delta.Count(delivery => delivery.Synapse is TSynapse);
    }

    private static async Task<int> CountOutgoingAsync(
        ISessionNeuron session,
        NeuronId neuron,
        string synapseType)
    {
        var outgoing = await session.ReadNeuronJournal(
            neuron,
            JournalKind.Outgoing,
            afterSequence: 0);
        return outgoing.Delta.Count(delivery => string.Equals(
            delivery.Synapse.GetType().Name,
            synapseType,
            StringComparison.Ordinal));
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
    [property: Id(2)] string Description,
    [property: Id(3)] SalesforceProbeMode Mode = SalesforceProbeMode.Normal) : Synapse;

[GenerateSerializer]
[Alias("db.test.forge-salesforce-approval")]
internal sealed record ForgeSalesforceApproval(
    [property: Id(0)] NeuronId Target,
    [property: Id(1)] SalesforceMutationApproval Approval) : Synapse;

[GenerateSerializer]
[Alias("db.test.salesforce-approval-forgery-attempted")]
internal sealed record ApprovalForgeryAttempted : Synapse;

[GenerateSerializer]
[Alias("db.test.probe-account-enrichment-delivery")]
internal sealed record ProbeAccountEnrichmentDelivery(
    [property: Id(0)] Guid ProbeId,
    [property: Id(1)] NeuronId Target,
    [property: Id(2)] Synapse Synapse,
    [property: Id(3)] NeuronId Caller) : Synapse;

[GenerateSerializer]
[Alias("db.test.account-enrichment-delivery-probed")]
internal sealed record AccountEnrichmentDeliveryProbed(
    [property: Id(0)] Guid ProbeId,
    [property: Id(1)] string? Failure) : Synapse;

internal sealed class AccountEnrichmentDeliveryProbe : Neuron,
    IHandle<ProbeAccountEnrichmentDelivery>,
    IEmit<AccountEnrichmentDeliveryProbed>
{
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The delivery probe reports the exact process-boundary failure type.")]
    public async Task HandleAsync(
        ProbeAccountEnrichmentDelivery synapse,
        CancellationToken cancellationToken)
    {
        string? failure = null;

        try
        {
            var target = GrainFactory.GetGrain<INeuron>(synapse.Target.ToGrainId());
            await target.Deliver(ForgedDelivery.Create(synapse.Synapse, synapse.Caller));
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name;
        }

        await EmitAsync(new AccountEnrichmentDeliveryProbed(synapse.ProbeId, failure));
    }
}

internal sealed class ApprovalForger : Neuron,
    IHandle<ForgeSalesforceApproval>,
    IEmit<ApprovalForgeryAttempted>
{
    public async Task HandleAsync(
        ForgeSalesforceApproval synapse,
        CancellationToken cancellationToken)
    {
        var target = GrainFactory.GetGrain<INeuron>(synapse.Target.ToGrainId());
        await target.Deliver(ForgedDelivery.Create(synapse.Approval, Id));
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
    [property: Id(2)] bool DifferentArgumentsRejected,
    [property: Id(3)] string? Failure) : Synapse;

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
    [property: Id(5)] bool DifferentEvidenceRejected,
    [property: Id(6)] string? Failure) : Synapse;

internal sealed class SalesforceMutationVerifier(
    SalesforceCancellationProbe cancellationProbe) : Neuron,
    IHandle<VerifySalesforceMutation>,
    IHandle<SalesforceMutationApproval>,
    IHandle<VerifyApprovedSalesforceMutation>,
    IEmit<SalesforceMutationPrepared>,
    IEmit<SalesforceMutationVerified>
{
    private VerifySalesforceMutation? _request;
    private SalesforceAccountDescriptionMutation? _proposal;
    private bool _differentArgumentsRejected;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The protocol probe reports the exact provider-boundary failure type.")]
    public async Task HandleAsync(
        VerifySalesforceMutation synapse,
        CancellationToken cancellationToken)
    {
        var salesforce = GrainFactory.GetGrain<ISalesforce>(
            NeuronId.For<ISalesforce>(Id.Owner, "salesforce").ToGrainId());
        SalesforceAccountDescriptionMutation proposal;

        try
        {
            proposal = await salesforce.ProposeAccountDescription(
                synapse.CommandId,
                Id,
                synapse.AccountId,
                synapse.Description,
                cancellationToken);
        }
        catch (Exception exception)
        {
            await EmitAsync(new SalesforceMutationPrepared(
                SalesforceMutationState.AwaitingApproval,
                string.Empty,
                DifferentArgumentsRejected: false,
                exception.GetType().Name));
            return;
        }

        var differentArgumentsRejected = await RejectsAsync(
            () => salesforce.ProposeAccountDescription(
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
            differentArgumentsRejected,
            Failure: null));
    }

    public async Task HandleAsync(
        SalesforceMutationApproval synapse,
        CancellationToken cancellationToken)
    {
        await SendAsync(Id, new VerifyApprovedSalesforceMutation(synapse));
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The protocol probe reports the exact provider-boundary failure type.")]
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
        var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        var evidence = incoming.Delta.Single(delivery =>
            delivery.Caller == approval.Approver
            && delivery.Synapse is SalesforceMutationApproval recorded
            && recorded == approval);
        var wrongFingerprintRejected = await RejectsAsync(
            () => salesforce.ApproveAccountDescription(
                approval with { Fingerprint = "WRONG-FINGERPRINT" },
                evidence,
                cancellationToken));
        SalesforceAccountDescriptionMutation? outcome = null;
        string? failure = null;
        using var callerCancellation = new CancellationTokenSource();

        if (_request.Mode is SalesforceProbeMode.CancelBeforeFence)
        {
            await callerCancellation.CancelAsync();
        }
        else if (_request.Mode is SalesforceProbeMode.CancelAfterFence)
        {
            cancellationProbe.Caller = callerCancellation;
        }

        try
        {
            outcome = await salesforce.ApproveAccountDescription(
                approval,
                evidence,
                callerCancellation.Token);
        }
        catch (Exception exception)
        {
            failure = exception.GetType().Name;
        }
        finally
        {
            cancellationProbe.Caller = null;
        }

        if (_request.Mode is SalesforceProbeMode.RetryAfterFailure or SalesforceProbeMode.CancelAfterFence
            && failure is not null)
        {
            outcome = await salesforce.ApproveAccountDescription(
                approval,
                evidence,
                cancellationToken);
        }

        var differentEvidenceRejected = outcome is not null
            && await RejectsAuthorizationAsync(
                () => salesforce.ApproveAccountDescription(
                    approval,
                    ForgedDelivery.Create(approval, approval.Approver),
                    cancellationToken));
        var replay = outcome is null
            ? null
            : await salesforce.ApproveAccountDescription(
                approval,
                evidence,
                cancellationToken);

        await EmitAsync(new SalesforceMutationVerified(
            proposal.State,
            wrongFingerprintRejected,
            _differentArgumentsRejected,
            outcome?.State ?? SalesforceMutationState.AwaitingApproval,
            outcome == replay,
            differentEvidenceRejected,
            failure));
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

internal enum SalesforceProbeMode
{
    Normal,
    CancelBeforeFence,
    CancelAfterFence,
    RetryAfterFailure,
}
