using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using DigitalBrain.Runtime.Neurons;
using Npgsql;

namespace DigitalBrain.SDK.Postgres;

public sealed class SynapseToPostgresMapper(INpgsqlConnectionFactory connectionFactory)
{
    private static readonly ConcurrentDictionary<Type, string> TableNameCache = new();
    private static readonly ConcurrentDictionary<Type, List<(PropertyInfo Property, string ColumnName, string DbType)>> PropertyCache = new();

    public async Task PersistSynapseAsync(string databaseId, Synapse synapse, CancellationToken cancellationToken = default)
    {
        var type = synapse.GetType();
        var tableName = GetTableName(type);
        var properties = GetProperties(type);

        await using var connection = await connectionFactory.OpenConnectionAsync(databaseId, cancellationToken);

        // 1. Auto-schema DDL generation and execution
        await EnsureTableExistsAsync(connection, tableName, properties, cancellationToken);

        // 2. Auto-upsert serialization and execution
        await UpsertSynapseAsync(connection, tableName, properties, synapse, cancellationToken);
    }

    public string GetTableName(Type type)
    {
        return TableNameCache.GetOrAdd(type, t =>
        {
            var name = t.Name;
            if (name.EndsWith("Synapse"))
                name = name[..^7];
            return "synapse_" + ToSnakeCase(name);
        });
    }

    private List<(PropertyInfo Property, string ColumnName, string DbType)> GetProperties(Type type)
    {
        return PropertyCache.GetOrAdd(type, t =>
        {
            var list = new List<(PropertyInfo Property, string ColumnName, string DbType)>();
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in props)
            {
                if (prop.Name == nameof(Synapse.Headers)) continue;

                var colName = ToSnakeCase(prop.Name);
                var dbType = MapToPostgresType(prop.PropertyType);
                list.Add((prop, colName, dbType));
            }
            return list;
        });
    }

    private static string MapToPostgresType(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string)) return "TEXT";
        if (type == typeof(Guid)) return "UUID";
        if (type == typeof(int)) return "INTEGER";
        if (type == typeof(long)) return "BIGINT";
        if (type == typeof(double)) return "DOUBLE PRECISION";
        if (type == typeof(float)) return "REAL";
        if (type == typeof(bool)) return "BOOLEAN";
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset)) return "TIMESTAMPTZ";
        
        return "JSONB"; // Fallback for complex/nested objects
    }

    private static string ToSnakeCase(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var startWithLower = char.ToLowerInvariant(input[0]) + input[1..];
        return string.Concat(startWithLower.Select((x, i) => i > 0 && char.IsUpper(x) ? "_" + char.ToLowerInvariant(x) : x.ToString()));
    }

    private async Task EnsureTableExistsAsync(
        NpgsqlConnection connection, 
        string tableName, 
        List<(PropertyInfo Property, string ColumnName, string DbType)> properties, 
        CancellationToken cancellationToken)
    {
        var columnsDefinition = new List<string>
        {
            "synapse_id UUID PRIMARY KEY",
            "correlation_id UUID NOT NULL",
            "causation_id UUID",
            "caller_neuron_id UUID NOT NULL",
            "caller_neuron_type TEXT",
            "receiver_neuron_id UUID NOT NULL",
            "receiver_neuron_type TEXT NOT NULL",
            "timestamp TIMESTAMPTZ NOT NULL",
            "traceparent TEXT",
            "tracestate TEXT"
        };

        foreach (var prop in properties)
        {
            var normalizedCol = prop.ColumnName;
            if (new[] { "synapse_id", "correlation_id", "causation_id", "caller_neuron_id", "caller_neuron_type", "receiver_neuron_id", "receiver_neuron_type", "timestamp", "traceparent", "tracestate" }.Contains(normalizedCol))
                continue;

            columnsDefinition.Add($"{normalizedCol} {prop.DbType}");
        }

        var ddl = $"CREATE TABLE IF NOT EXISTS {tableName} (\n  {string.Join(",\n  ", columnsDefinition)}\n);";

        await using var command = connection.CreateCommand();
        command.CommandText = ddl;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertSynapseAsync(
        NpgsqlConnection connection, 
        string tableName, 
        List<(PropertyInfo Property, string ColumnName, string DbType)> properties, 
        Synapse synapse, 
        CancellationToken cancellationToken)
    {
        var standardColumns = new List<(string Name, object? Value)>
        {
            ("synapse_id", synapse.SynapseId),
            ("correlation_id", synapse.CorrelationId),
            ("causation_id", synapse.CausationId),
            ("caller_neuron_id", synapse.CallerNeuronId),
            ("caller_neuron_type", synapse.CallerNeuronType),
            ("receiver_neuron_id", synapse.ReceiverNeuronId),
            ("receiver_neuron_type", synapse.ReceiverNeuronType),
            ("timestamp", synapse.Timestamp),
            ("traceparent", synapse.Traceparent),
            ("tracestate", synapse.Tracestate)
        };

        var insertCols = new List<string>();
        var updateCols = new List<string>();
        var paramNames = new List<string>();
        var parameters = new List<(string Name, object? Value, string DbType)>();

        foreach (var col in standardColumns)
        {
            insertCols.Add(col.Name);
            paramNames.Add("@" + col.Name);
            updateCols.Add($"{col.Name} = EXCLUDED.{col.Name}");
            parameters.Add((col.Name, col.Value, col.Name == "synapse_id" ? "UUID" : col.Name.EndsWith("_id") ? "UUID" : col.Name == "timestamp" ? "TIMESTAMPTZ" : "TEXT"));
        }

        foreach (var prop in properties)
        {
            var normalizedCol = prop.ColumnName;
            if (standardColumns.Any(c => c.Name == normalizedCol))
                continue;

            insertCols.Add(normalizedCol);
            paramNames.Add("@" + normalizedCol);
            updateCols.Add($"{normalizedCol} = EXCLUDED.{normalizedCol}");

            var val = prop.Property.GetValue(synapse);
            if (val != null && prop.DbType == "JSONB")
            {
                val = JsonSerializer.Serialize(val);
            }

            parameters.Add((normalizedCol, val, prop.DbType));
        }

        var sql = $@"
            INSERT INTO {tableName} ({string.Join(", ", insertCols)})
            VALUES ({string.Join(", ", paramNames)})
            ON CONFLICT (synapse_id) DO UPDATE
            SET {string.Join(", ", updateCols)};";

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var param in parameters)
        {
            var p = command.CreateParameter();
            p.ParameterName = "@" + param.Name;
            p.Value = param.Value ?? DBNull.Value;
            command.Parameters.Add(p);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
