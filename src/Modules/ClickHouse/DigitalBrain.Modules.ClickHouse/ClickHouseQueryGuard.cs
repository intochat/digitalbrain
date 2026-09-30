using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.ClickHouse;

// Client-side defence in depth in front of the server's readonly and resource caps: one SELECT
// (or WITH … SELECT), no comments, no second statement, no FORMAT/SETTINGS clause, no write
// verbs and no table functions that reach outside the database. readonly=2 does not stop a
// SELECT from fetching an arbitrary URL, so the table-function list is part of the safety model.
internal static partial class ClickHouseQueryGuard
{
    public const string Reason = "Use one read-only SELECT (or WITH … SELECT). Comments, multiple statements, FORMAT/SETTINGS clauses and writes are not allowed.";
    private const int MaxLength = 20_000;

    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL must not be blank. " + Reason, nameof(sql));
        }

        if (sql.Length > MaxLength)
        {
            throw new ArgumentException($"SQL exceeds {MaxLength} characters. " + Reason, nameof(sql));
        }

        var syntax = MaskQuoted(sql);
        if (!ReadStatement().IsMatch(syntax) || WriteOrControl().IsMatch(syntax) || ExternalTableFunction().IsMatch(syntax))
        {
            throw Invalid();
        }
    }

    // Replaces the content of every string literal and quoted identifier with a placeholder so the
    // token checks never fire on text, and rejects statement separators, comments and dollar-quoted
    // strings ($tag$…$tag$ would let a quote inside desynchronise this scan from the server's) outside them.
    internal static string MaskQuoted(string sql)
    {
        var syntax = new StringBuilder(sql.Length);
        char? quote = null;
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (quote is { } open)
            {
                if (character == '\\' && index + 1 < sql.Length)
                {
                    index++;
                }
                else if (character == open)
                {
                    // A doubled quote is an escaped quote inside the literal.
                    if (index + 1 < sql.Length && sql[index + 1] == open)
                    {
                        index++;
                    }
                    else
                    {
                        quote = null;
                        syntax.Append(open);
                    }
                }

                continue;
            }

            switch (character)
            {
                case '\'' or '"' or '`':
                    quote = character;
                    syntax.Append(character).Append('?');
                    break;
                case ';' or '#' or '$':
                    throw Invalid();
                case '-' when index + 1 < sql.Length && sql[index + 1] == '-':
                case '/' when index + 1 < sql.Length && sql[index + 1] == '*':
                    throw Invalid();
                default:
                    syntax.Append(character);
                    break;
            }
        }

        if (quote is not null)
        {
            throw Invalid();
        }

        return syntax.ToString();
    }

    private static ArgumentException Invalid() => new(Reason, "sql");

    [GeneratedRegex(@"\A\s*(?:SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadStatement();

    // Keywords followed by "(" are functions such as format('{}', x), not clauses.
    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE|TRUNCATE|RENAME|ATTACH|DETACH|OPTIMIZE|SYSTEM|KILL|GRANT|REVOKE|SET|SETTINGS|FORMAT|EXCHANGE|MOVE)\b(?!\s*\()|\bINTO\s+OUTFILE\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WriteOrControl();

    // Base names plus the S3/Azure/HDFS/Local/Cluster variants ClickHouse derives from them.
    [GeneratedRegex(@"\b(?:url|s3|oss|cosn|gcs|azureBlobStorage|file|remote|remoteSecure|cluster|clusterAllReplicas|mysql|postgresql|mongodb|redis|sqlite|jdbc|odbc|hdfs|hive|input|executable|deltaLake|iceberg|hudi|ytsaurus|arrowFlight|timeSeries|loop|fuzzQuery|dictionary)(?:S3|Azure|HDFS|Local|Cluster)*\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExternalTableFunction();
}
