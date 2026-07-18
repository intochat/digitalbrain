using DigitalBrain.Runtime;
using DigitalBrain.Runtime.Neurons;
using Microsoft.Data.Sqlite;
using Orleans.Journaling;

namespace DigitalBrain.SDK.Sqlite.Sqlite;

[ImplicitStreamSubscription(SqliteNeuronType)]
internal sealed class SqliteNeuron(
    [FromKeyedServices("incoming")] IDurableList<Synapse> incoming,
    [FromKeyedServices("outgoing")] IDurableList<Synapse> outgoing,
    IGrainFactory grains,
    IDatabaseContextFactory databases,
    ILogger<SqliteNeuron> logger)
    : Neuron(incoming, outgoing, grains, logger),
      ISqlite,
      INeuronMetadata,
      IStorageNeuron,
      IHandle<SqliteExecRequest>,
      IHandle<SqliteQueryRequest>
{
    public const string SqliteNeuronType = nameof(SqliteNeuron);

    public static NeuronId Id => new("data/sqlite");
    public static string Icon => "sqlite";
    public static NeuronCapability Capabilities => NeuronCapability.Storage;

    protected override async Task HandleSynapseAsync(Synapse synapse)
    {
        switch (synapse)
        {
            case SqliteExecRequest exec: await HandleExecAsync(exec); break;
            case SqliteQueryRequest query: await HandleQueryAsync(query); break;
        }
    }

    async Task HandleExecAsync(SqliteExecRequest request)
    {
        await using var connection = await databases.OpenAsync(request.DatabaseId, CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = request.Sql;
        BindParameters(command, request.Parameters);

        var rowsAffected = await command.ExecuteNonQueryAsync();

        long lastInsertRowId = 0;
        await using (var idCommand = connection.CreateCommand())
        {
            idCommand.CommandText = "SELECT last_insert_rowid();";
            lastInsertRowId = Convert.ToInt64(await idCommand.ExecuteScalarAsync() ?? 0L);
        }

        await FireSynapseAsync(new SqliteExecResponse(RowsAffected: rowsAffected,
        LastInsertRowId: lastInsertRowId) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }

    async Task HandleQueryAsync(SqliteQueryRequest request)
    {
        await using var connection = await databases.OpenAsync(request.DatabaseId, CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = request.Sql;
        BindParameters(command, request.Parameters);

        await using var reader = await command.ExecuteReaderAsync();

        var columns = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
            columns[i] = reader.GetName(i);

        var rows = new List<IReadOnlyList<string?>>();
        while (await reader.ReadAsync())
        {
            var row = new string?[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
            rows.Add(row);
        }

        await FireSynapseAsync(new SqliteQueryResponse(Columns: columns,
        Rows: rows) { Headers = SynapseMetadata.Create(
            synapseId: Guid.NewGuid(),
            correlationId: request.CorrelationId,
            causationId: request.SynapseId,
            callerNeuronId: default,
            callerNeuronType: null,
            receiverNeuronId: request.CallerNeuronId,
            receiverNeuronType: request.CallerNeuronType ?? "External",
            timestamp: default
        ) });
    }

    static void BindParameters(SqliteCommand command, IReadOnlyList<SqliteParameterValue>? parameters)
    {
        if (parameters is null) return;
        foreach (var p in parameters)
        {
            object value = (object?)p.StringValue
                ?? p.IntegerValue
                ?? p.RealValue
                ?? (object?)p.BlobValue
                ?? DBNull.Value;
            command.Parameters.AddWithValue("@" + p.Name, value);
        }
    }
}
