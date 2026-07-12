extern alias RuntimeMigrationProject;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DigitalBrain.Core.Runtime;
using DigitalBrain.Kernel.Runtime;
using Microsoft.Extensions.Configuration;
using Migration = RuntimeMigrationProject::DigitalBrain.RuntimeMigration;

namespace DigitalBrain.Tests.RuntimeMigration;

public sealed class RuntimeMigrationTests
{
    [Fact]
    public void ModeParser_RequiresOneExplicitNonSecretMode()
    {
        Assert.Equal(Migration.MigrationMode.DryRun, Migration.MigrationModeParser.Parse(["--dry-run"]));
        Assert.Equal(Migration.MigrationMode.Apply, Migration.MigrationModeParser.Parse(["--apply"]));
        Assert.Equal("mode-required", Assert.Throws<Migration.MigrationGapException>(
            () => Migration.MigrationModeParser.Parse([])).Code);
        Assert.Equal("mode-required", Assert.Throws<Migration.MigrationGapException>(
            () => Migration.MigrationModeParser.Parse(["--apply", "unexpected"])).Code);
    }

    [Fact]
    public async Task CommandFailureOutputContainsOnlyTheBoundedStatusContract()
    {
        var writer = new StringWriter();

        var exitCode = await Migration.RuntimeMigrationCommand.RunAsync(["unexpected-value"], writer);

        Assert.Equal(2, exitCode);
        Assert.Equal(
            "schema=1" + Environment.NewLine +
            "migration_status=blocked" + Environment.NewLine +
            "migration_gap=mode-required" + Environment.NewLine,
            writer.ToString());
        Assert.DoesNotContain("unexpected-value", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ReadOnlyVerifier_AcceptsAnAuthenticatedChainWithoutChangingIt()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.jsonl");
            var key = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
            WriteAuthenticatedJournal(path, "migration.test", key, "record", new { value = 1 });
            var before = File.ReadAllBytes(path);

            using var reader = new Migration.ReadOnlyAuthenticatedJournalReader("migration.test", key, path);
            var verified = reader.Read();

            Assert.Single(verified.Records);
            Assert.Equal("record", verified.Records[0].Kind);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.False(File.Exists(path + ".quarantine"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReadOnlyVerifier_RejectsTamperingWithoutRepairOrQuarantine()
    {
        var directory = TemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "journal.jsonl");
            var key = Enumerable.Repeat((byte)7, 32).ToArray();
            WriteAuthenticatedJournal(path, "migration.test", key, "record", new { value = 1 });
            var tampered = File.ReadAllText(path).Replace("\"value\":1", "\"value\":2", StringComparison.Ordinal);
            File.WriteAllText(path, tampered, new UTF8Encoding(false));

            using var reader = new Migration.ReadOnlyAuthenticatedJournalReader("migration.test", key, path);
            var exception = Assert.Throws<Migration.MigrationGapException>(() => reader.Read());

            Assert.Equal("journal-authentication-failed", exception.Code);
            Assert.False(File.Exists(path + ".quarantine"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteAuthenticatedJournal(
        string path,
        string domain,
        byte[] key,
        string kind,
        object payload)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var previousDigest = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes("digitalbrain-journal-genesis\n" + domain)));
        var payloadElement = JsonSerializer.SerializeToElement(payload, options);
        var body = new TestJournalBody(1, domain, 1, kind, previousDigest, payloadElement);
        var bodyBytes = JsonSerializer.SerializeToUtf8Bytes(body, options);
        var digest = Convert.ToHexString(SHA256.HashData(bodyBytes));
        var authenticationCode = Convert.ToHexString(HMACSHA256.HashData(key, bodyBytes));
        var envelope = new TestJournalEnvelope(
            "digitalbrain.authenticated-jsonl.v1",
            1,
            domain,
            1,
            kind,
            previousDigest,
            payloadElement,
            digest,
            authenticationCode);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(envelope, options) + "\n",
            new UTF8Encoding(false));

        var anchorBody = new TestJournalAnchorBody(1, domain, 1, digest);
        var anchorAuthentication = Convert.ToHexString(HMACSHA256.HashData(
            key,
            JsonSerializer.SerializeToUtf8Bytes(anchorBody, options)));
        File.WriteAllText(
            path + ".head",
            JsonSerializer.Serialize(new TestJournalAnchor(1, domain, 1, digest, anchorAuthentication), options),
            new UTF8Encoding(false));
    }

    private sealed record TestJournalBody(
        int Version,
        string Domain,
        long Sequence,
        string Kind,
        string PreviousDigest,
        JsonElement Payload);

    private sealed record TestJournalEnvelope(
        [property: JsonPropertyName("$journal")] string Marker,
        int Version,
        string Domain,
        long Sequence,
        string Kind,
        string PreviousDigest,
        JsonElement Payload,
        string Digest,
        string AuthenticationCode);

    private sealed record TestJournalAnchorBody(
        int Version,
        string Domain,
        long Sequence,
        string Digest);

    private sealed record TestJournalAnchor(
        int Version,
        string Domain,
        long Sequence,
        string Digest,
        string AuthenticationCode);

    [Fact]
    public void Planner_PreservesTurnsAndExactInternalAuthorizationContinuation()
    {
        var (operations, conversations) = LegacyJournals(
            new ToolAction("openUrl", "Connect", "/oauth/start/google?f=abcdefghijklmnopqrstuvwxyzABCDEF"));

        var plan = new Migration.LegacyMigrationPlanner().Plan(operations, conversations);

        var conversation = Assert.Single(plan.Conversations);
        var operation = Assert.Single(conversation.Operations).Destination;
        Assert.Equal(2, conversation.Turns.Count);
        Assert.Equal(ConversationOperationStatus.AwaitingAuthorization, operation.Status);
        Assert.Equal("google", operation.SuspendedInvocation!.Provider);
        Assert.Equal("gmail.search", operation.SuspendedInvocation.ToolId);
        Assert.Equal("0123456789abcdef0123456789abcdef", operation.SuspendedInvocation.AuthorizationAttemptId);
        Assert.Equal("abcdefghijklmnopqrstuvwxyzABCDEF", operation.SuspendedInvocation.AuthorizationFlowReference);
        Assert.Equal("{\"query\":\"from:example\"}", Encoding.UTF8.GetString(operation.SuspendedInvocation.InputUtf8));
        Assert.Equal(new DateTimeOffset(2030, 1, 1, 0, 10, 0, TimeSpan.Zero),
            operation.SuspendedInvocation.AuthorizationExpiresAt);

        var expected = Migration.ConversationMigrationApplier.ExpectedState(conversation);
        Assert.Empty(expected.Outbox);
        Assert.Equal(2, expected.Turns.Length);
        Assert.Equal(conversation.MigrationId, Assert.Single(expected.AppliedMigrationIds));
        var reopened = Assert.Single(expected.Operations).SuspendedInvocation!;
        Assert.Equal(operation.SuspendedInvocation.Provider, reopened.Provider);
        Assert.Equal(operation.SuspendedInvocation.ToolId, reopened.ToolId);
        Assert.Equal(operation.SuspendedInvocation.AuthorizationAttemptId, reopened.AuthorizationAttemptId);
        Assert.Equal(operation.SuspendedInvocation.AuthorizationExpiresAt, reopened.AuthorizationExpiresAt);
        Assert.Equal(operation.SuspendedInvocation.AuthorizationFlowReference, reopened.AuthorizationFlowReference);
        Assert.Equal(operation.SuspendedInvocation.InputUtf8, reopened.InputUtf8);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void Planner_NormalizesSupportedLegacyConversationVersionsWithoutDroppingTurns(int version)
    {
        var (operations, conversations) = LegacyJournals(
            new ToolAction("openUrl", "Connect", "/oauth/start/google?f=abcdefghijklmnopqrstuvwxyzABCDEF"));
        var record = Assert.Single(conversations.Records);
        var persisted = JsonSerializer.Deserialize<Migration.LegacyPersistedConversation>(record.Payload)! with
        {
            Version = version
        };
        conversations = conversations with
        {
            Records = [record with { Payload = JsonSerializer.Serialize(persisted) }]
        };

        var plan = new Migration.LegacyMigrationPlanner().Plan(operations, conversations);

        Assert.Equal(2, Assert.Single(plan.Conversations).Turns.Count);
    }

    [Fact]
    public void Planner_PreservesADistinctLegacyIdempotencyKey()
    {
        const string idempotency = "legacy-idempotency-1";
        var (operations, conversations) = LegacyJournals(
            new ToolAction("openUrl", "Connect", "/oauth/start/google?f=abcdefghijklmnopqrstuvwxyzABCDEF"),
            idempotency);

        var conversation = Assert.Single(new Migration.LegacyMigrationPlanner().Plan(operations, conversations).Conversations);
        var expected = Migration.ConversationMigrationApplier.ExpectedState(conversation);

        Assert.Equal(idempotency, Assert.Single(conversation.Operations).Destination.CommandId);
        Assert.Equal(idempotency, Assert.Single(expected.Inbox).CommandId);
        Assert.Equal(idempotency, Assert.Single(expected.Turns, turn => turn.Kind == ConversationTurnKind.User).IdempotencyKey);
        Assert.Equal(
            Assert.Single(expected.Operations).OperationId,
            Assert.Single(expected.Turns, turn => turn.Kind == ConversationTurnKind.Assistant).IdempotencyKey);
        Assert.Equal(conversation.ExpectedDigest, Migration.MigrationHash.ConversationDigest(expected));
    }

    [Fact]
    public void Planner_RejectsProviderAuthorizationUrls()
    {
        var (operations, conversations) = LegacyJournals(
            new ToolAction("openUrl", "Connect", "https://accounts.google.com/o/oauth2/auth?state=opaque"));

        var exception = Assert.Throws<Migration.MigrationGapException>(() =>
            new Migration.LegacyMigrationPlanner().Plan(operations, conversations));

        Assert.Equal("authorization-flow-unrepresentable", exception.Code);
    }

    [Fact]
    public void Planner_AcceptsAnAbsoluteInternalStartOnlyAtTheConfiguredOrigin()
    {
        var (operations, conversations) = LegacyJournals(new ToolAction(
            "openUrl",
            "Connect",
            "https://runtime.example/oauth/start/google?f=abcdefghijklmnopqrstuvwxyzABCDEF"));

        var plan = new Migration.LegacyMigrationPlanner(new Uri("https://runtime.example/"))
            .Plan(operations, conversations);
        var invocation = Assert.Single(Assert.Single(plan.Conversations).Operations).Destination.SuspendedInvocation;

        Assert.Equal("abcdefghijklmnopqrstuvwxyzABCDEF", invocation!.AuthorizationFlowReference);
        Assert.Equal("authorization-flow-unrepresentable", Assert.Throws<Migration.MigrationGapException>(() =>
            new Migration.LegacyMigrationPlanner(new Uri("https://other.example/"))
                .Plan(operations, conversations)).Code);
    }

    [Fact]
    public void EveryInterruptedPrefixIsRecognizedAndTheFinalStateIsARerunNoOp()
    {
        var (operations, conversations) = LegacyJournals(
            new ToolAction("openUrl", "Connect", "/oauth/start/google?t=abcdefghijklmnopqrstuvwxyzABCDEF"));
        var conversation = Assert.Single(new Migration.LegacyMigrationPlanner()
            .Plan(operations, conversations).Conversations);
        var states = Migration.ConversationMigrationApplier.ExpectedStates(conversation);

        for (var index = 0; index < states.Count; index++)
            Assert.Equal(index, Migration.ConversationMigrationApplier.ResumeIndex(conversation, states[index]));

        var conflicting = states[^1] with { Revision = states[^1].Revision + 1 };
        Assert.Equal(-1, Migration.ConversationMigrationApplier.ResumeIndex(conversation, conflicting));
        Assert.Equal(states.Count - 1,
            Migration.ConversationMigrationApplier.ResumeIndex(conversation,
                Migration.ConversationMigrationApplier.ExpectedState(conversation)));
    }

    [Fact]
    public void MarkerCodec_AuthenticatesCiphertextAndReopensWithConfiguredKeys()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DigitalBrain:Profile"] = "Development",
            ["DigitalBrain:Runtime:State:ActiveKekVersion"] = "1",
            ["DigitalBrain:Runtime:State:Keks:1"] = Convert.ToBase64String(Enumerable.Repeat((byte)3, 32).ToArray()),
            ["DigitalBrain:Runtime:State:SigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)5, 32).ToArray())
        }).Build();
        using var keys = Migration.MigrationMarkerKeyRing.Load(configuration);
        var source = new string('a', 64);
        var marker = new Migration.MigrationMarker(1, source, "legacy-v2-" + source, new string('b', 64), 1, 2, 1, 0);
        const string binding = "opaque-binding";

        var encrypted = Migration.MigrationMarkerCodec.Encrypt(marker, binding, keys);
        Assert.DoesNotContain("legacy-v2", encrypted.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(source, encrypted.ToString(), StringComparison.Ordinal);
        Assert.Equal(marker, Migration.MigrationMarkerCodec.Decrypt(encrypted, binding, keys));
        var tampered = encrypted.ToArray();
        tampered[^2] ^= 1;
        Assert.Throws<Migration.MigrationGapException>(() =>
            Migration.MigrationMarkerCodec.Decrypt(BinaryData.FromBytes(tampered), binding, keys));
    }

    private static (Migration.VerifiedJournal Operations, Migration.VerifiedJournal Conversations) LegacyJournals(
        ToolAction action,
        string? idempotency = null)
    {
        const string commandId = "command-1";
        var context = new DigitalBrain.Core.Runtime.RequestContext(
            new TenantId("tenant"),
            new WorkspaceId("workspace"),
            new PrincipalRef("principal", PrincipalKind.User),
            "session",
            AuthAssurance.Password,
            "correlation",
            idempotency,
            new HashSet<string>(StringComparer.Ordinal));
        var input = JsonSerializer.SerializeToElement(new { query = "from:example" });
        var authorization = new ExternalAuthorizationContinuation(
            "google",
            new ToolInvocation("gmail.search", input),
            "0123456789abcdef0123456789abcdef",
            new DateTimeOffset(2030, 1, 1, 0, 10, 0, TimeSpan.Zero));
        var updatedAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var command = new CommandEnvelope(
            "ino.interact",
            2,
            commandId,
            context,
            JsonSerializer.SerializeToElement(new { prompt = "Find mail" }));
        var operation = new Migration.LegacyPersistedOperation(
            idempotency ?? commandId,
            context.TenantId.Value,
            context.WorkspaceId.Value,
            new DigitalBrain.Core.Runtime.OperationStatus(
                "legacy-operation",
                WorkflowState.AwaitingExternalAuthorization,
                null,
                updatedAt),
            command,
            authorization);
        var snapshot = new InoConversationSnapshot(
            InoConversationIdentity.From(context),
            4,
            [
                new InoConversationTurn(commandId, "user", "Find mail", InoConversationStates.AwaitingAuthorization),
                new InoConversationTurn(commandId, "assistant", "Connect to continue", InoConversationStates.AwaitingAuthorization)
            ],
            [
                new InoConversationOperation(
                    commandId,
                    "Find mail",
                    InoConversationStates.AwaitingAuthorization,
                    null,
                    true,
                    updatedAt,
                    action,
                    Authorization: authorization)
            ]);
        var persisted = new Migration.LegacyPersistedConversation(
            4,
            context.TenantId,
            context.WorkspaceId,
            context.Principal,
            snapshot);
        var digest = new string('a', 64);
        return (
            new Migration.VerifiedJournal(
                "digitalbrain.v2.operations",
                [new Migration.VerifiedJournalRecord(1, "operation.AwaitingExternalAuthorization",
                    JsonSerializer.Serialize(operation), false, digest)],
                1,
                digest),
            new Migration.VerifiedJournal(
                "digitalbrain.v2.ino-effects",
                [new Migration.VerifiedJournalRecord(1, "conversation.snapshot",
                    JsonSerializer.Serialize(persisted), false, digest)],
                1,
                digest));
    }

    private static string TemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "digitalbrain-runtime-migration-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
