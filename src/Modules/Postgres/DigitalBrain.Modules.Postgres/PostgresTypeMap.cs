using System.Diagnostics;
using System.Globalization;
using Npgsql;

namespace DigitalBrain.Postgres;

internal static class PostgresTypeMap
{
    public const string Text = "text";
    public const string Number = "number";
    public const string Date = "date";
    public const string Boolean = "boolean";

    public static string ToTableType(string type) => type switch
    {
        Text or Number or Date or Boolean => type,
        "bool" => Boolean,
        "int2" or "int4" or "int8" or "smallint" or "integer" or "bigint" or
        "float4" or "float8" or "real" or "double precision" or "numeric" or "decimal" or "money" => Number,
        _ => Text,
    };

}
