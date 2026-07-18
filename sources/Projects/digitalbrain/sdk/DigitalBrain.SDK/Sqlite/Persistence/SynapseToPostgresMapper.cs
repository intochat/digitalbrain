using System.Data.Common;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.Sqlite.Persistence;

public static class SynapseToPostgresMapper
{
    public static string ToSnakeCase(string str)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var builder = new StringBuilder();
        builder.Append(char.ToLower(str[0]));
        for (int i = 1; i < str.Length; i++)
        {
            char c = str[i];
            if (char.IsUpper(c))
            {
                builder.Append('_');
                builder.Append(char.ToLower(c));
            }
            else
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static string GetPgType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(Guid)) return "UUID";
        if (underlying == typeof(string)) return "TEXT";
        if (underlying == typeof(int)) return "INTEGER";
        if (underlying == typeof(long)) return "BIGINT";
        if (underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(decimal)) return "DOUBLE PRECISION";
        if (underlying == typeof(bool)) return "BOOLEAN";
        if (underlying == typeof(DateTimeOffset)) return "TIMESTAMP WITH TIME ZONE";
        if (underlying == typeof(DateTime)) return "TIMESTAMP";
        return "JSONB";
    }

    public static string GenerateCreateTableSql(Type synapseType, string tableName)
    {
        var properties = synapseType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var columns = new List<string>();

        // Always ensure synapse_id is the primary key and is not null
        foreach (var prop in properties)
        {
            if (prop.Name == "Headers") continue; // Skip raw headers record
            var colName = ToSnakeCase(prop.Name);
            var pgType = GetPgType(prop.PropertyType);
            if (prop.Name == "SynapseId")
            {
                columns.Add($"\"{colName}\" {pgType} NOT NULL PRIMARY KEY");
            }
            else
            {
                columns.Add($"\"{colName}\" {pgType}");
            }
        }

        return $"CREATE TABLE IF NOT EXISTS \"{tableName}\" (\n  {string.Join(",\n  ", columns)}\n);";
    }

    public static async Task AutoMapAndUpsertAsync(DbConnection connection, Synapse synapse, string tableName)
    {
        var synapseType = synapse.GetType();

        // 1. DDL generation and execution
        var createTableSql = GenerateCreateTableSql(synapseType, tableName);
        await using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = createTableSql;
            await createCmd.ExecuteNonQueryAsync();
        }

        // 2. Prepare Insert/Upsert SQL
        var properties = synapseType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                    .Where(p => p.Name != "Headers")
                                    .ToList();

        var columnNames = properties.Select(p => $"\"{ToSnakeCase(p.Name)}\"").ToList();
        var parameterNames = properties.Select(p => $"@{p.Name}").ToList();

        var upsertUpdates = properties.Where(p => p.Name != "SynapseId")
                                      .Select(p => $"\"{ToSnakeCase(p.Name)}\" = EXCLUDED.\"{ToSnakeCase(p.Name)}\"")
                                      .ToList();

        var upsertSql = new StringBuilder();
        upsertSql.AppendLine($"INSERT INTO \"{tableName}\" ({string.Join(", ", columnNames)})");
        upsertSql.AppendLine($"VALUES ({string.Join(", ", parameterNames)})");
        upsertSql.AppendLine("ON CONFLICT (\"synapse_id\") DO UPDATE SET");
        upsertSql.AppendLine($"  {string.Join(",\n  ", upsertUpdates)};");

        await using (var upsertCmd = connection.CreateCommand())
        {
            upsertCmd.CommandText = upsertSql.ToString();

            foreach (var prop in properties)
            {
                var val = prop.GetValue(synapse);
                var param = upsertCmd.CreateParameter();
                param.ParameterName = $"@{prop.Name}";

                if (val == null)
                {
                    param.Value = DBNull.Value;
                }
                else
                {
                    var pgType = GetPgType(prop.PropertyType);
                    if (pgType == "JSONB")
                    {
                        param.Value = JsonSerializer.Serialize(val);
                    }
                    else
                    {
                        param.Value = val;
                    }
                }
                upsertCmd.Parameters.Add(param);
            }

            await upsertCmd.ExecuteNonQueryAsync();
        }
    }
}
