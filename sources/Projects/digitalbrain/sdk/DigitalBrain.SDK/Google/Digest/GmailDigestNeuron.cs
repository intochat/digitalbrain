using System.Text;
using System.Text.Json;
using DigitalBrain.SDK.Google;
using DigitalBrain.Runtime.Ui;
using Orleans.Journaling;
using DigitalBrain.SDK.Sqlite;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;

namespace DigitalBrain.SDK.Google.Digest;

[ImplicitStreamSubscription(GmailDigestNeuronType)]
internal sealed class GmailDigestNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    [FromKeyedServices("digest-state")] IDurableValue<DigestState?> state,
    IGrainFactory grains,
    ISynapsePersistenceService synapsePersistence,
    ILogger<GmailDigestNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      IGmailDigest,
      INeuronMetadata,
      IExternalNeuron,
      IHandle<StoreLastNGmailSendersRequest>,
      IHandle<GmailSendersReady>,
      IHandle<SqliteExecResponse>
{
    public const string GmailDigestNeuronType = nameof(GmailDigestNeuron);

    public static NeuronId Id => new("google/gmail-digest");
    public static string Icon => "gmail";
    public static NeuronCapability Capabilities => NeuronCapability.External;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case StoreLastNGmailSendersRequest req: await HandleStartAsync(req); break;
            case GmailSendersReady ready:           await HandleSendersAsync(ready); break;
            case SqliteExecResponse exec:           await HandleExecResponseAsync(exec); break;
        }
    }

    async Task HandleStartAsync(StoreLastNGmailSendersRequest request)
    {
        state.Value = new DigestState(
            Pending: request,
            Senders: Array.Empty<GmailSender>(),
            ExecsExpected: 0,
            ExecsCompleted: 0);
        await WriteStateAsync();

        await FireSynapseAsync(new GetLastNGmailSendersRequest(UserAccountId: request.UserAccountId,
        N: request.N) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: GmailDigestNeuronType,
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: "GmailNeuron",
            timestamp: default
        ) });
    }

    async Task HandleSendersAsync(GmailSendersReady ready)
    {
        var current = state.Value;
        if (current is null) return;

        // 1. Directly persist the GmailSendersReady synapse using proper EF Core!
        await synapsePersistence.SaveSynapseAsync(ready, CancellationToken.None);

        // 2. One Sqlite exec covers the whole batch: schema-create + N upserts.
        // This populates the custom sqlite database for relational queries.
        var (sql, parameters) = BuildBatchSql(ready.Senders);
        state.Value = current with
        {
            Senders = ready.Senders,
            ExecsExpected = 1,
            ExecsCompleted = 0,
        };
        await WriteStateAsync();

        await FireSynapseAsync(new SqliteExecRequest(DatabaseId: current.Pending.DatabaseId,
        Sql: sql,
        Parameters: parameters) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: current.Pending.CorrelationId,
            causationId: ready.SynapseId,
            callerNeuronId: InstanceId,
            callerNeuronType: GmailDigestNeuronType,
            receiverNeuronId: Guid.NewGuid(),
            receiverNeuronType: "SqliteNeuron",
            timestamp: default
        ) });
    }

    async Task HandleExecResponseAsync(SqliteExecResponse exec)
    {
        var current = state.Value;
        if (current is null) return;

        var completed = current.ExecsCompleted + 1;
        state.Value = current with { ExecsCompleted = completed };
        await WriteStateAsync();

        if (completed < current.ExecsExpected) return;

        var pending = current.Pending;

        // Spec-first metric: declared by @telemetry:counter:gmail_digest_rows_written
        Counter("gmail_digest_rows_written").Increment(current.Senders.Count);

        await FireSynapseAsync(new GmailDigestReady(UserAccountId: pending.UserAccountId,
        DatabaseId: pending.DatabaseId,
        RowsWritten: current.Senders.Count) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: pending.CorrelationId,
            causationId: exec.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: pending.CallerNeuronId,
            receiverNeuronType: pending.CallerNeuronType ?? "External",
            timestamp: default
        ) });

        await FireSynapseAsync(new RfwCard(LibraryName: "digitalbrain",
        RootWidget: "DataTable",
        DataJson: BuildDataTableJson(pending.UserAccountId, current.Senders)) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: pending.CorrelationId,
            causationId: exec.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: Guid.Empty,
            receiverNeuronType: "External",
            timestamp: default
        ) });

        state.Value = null;
        await WriteStateAsync();
    }

    static (string Sql, IReadOnlyList<SqliteParameterValue> Parameters) BuildBatchSql(
        IReadOnlyList<GmailSender> senders)
    {
        var sql = new StringBuilder();
        sql.AppendLine("CREATE TABLE IF NOT EXISTS senders (");
        sql.AppendLine("  email TEXT PRIMARY KEY,");
        sql.AppendLine("  name TEXT,");
        sql.AppendLine("  received_utc TEXT,");
        sql.AppendLine("  subject TEXT");
        sql.AppendLine(");");

        var parameters = new List<SqliteParameterValue>(capacity: senders.Count * 4);
        for (var i = 0; i < senders.Count; i++)
        {
            sql.AppendLine(
                $"INSERT OR REPLACE INTO senders (email, name, received_utc, subject) " +
                $"VALUES (@email{i}, @name{i}, @received{i}, @subject{i});");
            var sender = senders[i];
            parameters.Add(new SqliteParameterValue($"email{i}",    sender.EmailAddress, null, null, null));
            parameters.Add(new SqliteParameterValue($"name{i}",     sender.Name,         null, null, null));
            parameters.Add(new SqliteParameterValue($"received{i}", sender.ReceivedUtc.ToString("O"), null, null, null));
            parameters.Add(new SqliteParameterValue($"subject{i}",  sender.Subject,      null, null, null));
        }

        return (sql.ToString(), parameters);
    }

    static string BuildDataTableJson(string user, IReadOnlyList<GmailSender> senders)
    {
        var rows = new object[senders.Count][];
        for (var i = 0; i < senders.Count; i++)
        {
            var s = senders[i];
            rows[i] = [s.Name, s.EmailAddress, s.ReceivedUtc.ToString("O"), s.Subject];
        }

        return JsonSerializer.Serialize(new
        {
            title = $"Last {senders.Count} senders for {user}",
            columns = new[] { "Name", "Email", "Received", "Subject" },
            rows,
        });
    }
}

[GenerateSerializer]
public sealed record DigestState(
    [property: Id(0)] StoreLastNGmailSendersRequest Pending,
    [property: Id(1)] IReadOnlyList<GmailSender> Senders,
    [property: Id(2)] int ExecsExpected,
    [property: Id(3)] int ExecsCompleted);
