using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Postgres;

internal static class PostgresTablePolicy
{
    public static string PhysicalName(string scope, string key)
        => "dbt_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + key)))[..56];

    public static void Identifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 63 || !(char.IsAsciiLetter(name[0]) || name[0] == '_')
            || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        { throw new PostgresQueryException("Identifiers must contain 1–63 ASCII letters, digits or underscores and must not start with a digit."); }
    }

    public static string SqlType(string type) => type switch
    {
        "text" => "text",
        "number" => "numeric",
        "boolean" => "boolean",
        "timestamptz" => "timestamptz",
        "jsonb" => "jsonb",
        _ => throw new PostgresQueryException("Column type must be text, number, boolean, timestamptz or jsonb.")
    };

    public static TableDefinition Validate(TableDefinition definition)
    {
        if (definition?.Columns is not { Length: > 0 and <= 32 } || definition.PrimaryKey is not { Length: > 0 })
        { throw new PostgresQueryException("Provide 1–32 columns and at least one primary-key column."); }
        foreach (var column in definition.Columns)
        {
            if (column is null) { throw new PostgresQueryException("Columns cannot be null."); }
            Identifier(column.Name);
            _ = SqlType(column.Type);
        }
        if (definition.Columns.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != definition.Columns.Length
            || definition.PrimaryKey.Distinct(StringComparer.Ordinal).Count() != definition.PrimaryKey.Length
            || definition.PrimaryKey.Any(k => !definition.Columns.Any(c => c.Name == k)))
        { throw new PostgresQueryException("Column names and primary keys must be unique; primary keys must name defined columns."); }
        return new(definition.Columns.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray(), definition.PrimaryKey.Order(StringComparer.Ordinal).ToArray());
    }

    public static bool Compatible(TableDefinition left, TableDefinition right)
        => left.Columns.SequenceEqual(right.Columns) && left.PrimaryKey.SequenceEqual(right.PrimaryKey);

    public static TableValue[] Values(TableDefinition definition, TableValue[] values, bool key)
    {
        var columns = definition.Columns.Where(c => definition.PrimaryKey.Contains(c.Name) == key).ToArray();
        if (values is null || values.Length != columns.Length || values.Any(v => v is null)
            || values.Select(v => v.Column).Distinct(StringComparer.Ordinal).Count() != values.Length)
        { throw new PostgresQueryException("Supply every key column separately and every non-key column exactly once."); }
        return columns.Select(column =>
        {
            var value = values.SingleOrDefault(v => v.Column == column.Name)
                ?? throw new PostgresQueryException("Values must name the defined columns.");
            if (value.Json is null || value.Json.Length > 1_000_000) { throw new PostgresQueryException("Each value must be valid JSON of at most 1 MB."); }
            try
            {
                using var document = JsonDocument.Parse(value.Json);
                var element = document.RootElement;
                var valid = element.ValueKind == JsonValueKind.Null ? !key : column.Type switch
                {
                    "text" => element.ValueKind == JsonValueKind.String,
                    "number" => element.ValueKind == JsonValueKind.Number,
                    "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "timestamptz" => element.ValueKind == JsonValueKind.String && element.GetString()!.Length > 10 && DateTimeOffset.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                        && (element.GetString()!.EndsWith('Z') || element.GetString()![10..].Contains('+') || element.GetString()![10..].Contains('-')),
                    "jsonb" => true,
                    _ => false
                };
                if (!valid) { throw new PostgresQueryException($"Value for {column.Name} must match {column.Type}; keys cannot be null and timestamps require a timezone."); }
                return value;
            }
            catch (JsonException) { throw new PostgresQueryException("Values must contain valid JSON."); }
        }).ToArray();
    }
}
