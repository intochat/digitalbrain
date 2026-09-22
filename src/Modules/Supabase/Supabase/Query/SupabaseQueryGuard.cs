using System.Text;
using System.Text.RegularExpressions;

namespace DigitalBrain.Supabase;

// Syntax restrictions improve errors; PostgreSQL READ ONLY and database role permissions enforce access.
internal static partial class SupabaseQueryGuard
{
    public const string Reason = "Use one read-only PostgreSQL SELECT (or WITH … SELECT). Writes, session control, comments and statement separators are not allowed.";

    public static string Normalize(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 20_000) { throw Invalid(); }
        // Accept the optional statement terminator, but never an embedded separator.
        // The returned SQL is embedded in subqueries for description, paging and counts.
        sql = sql.Trim();
        if (sql.EndsWith(';')) { sql = sql[..^1].TrimEnd(); }
        var syntax = MaskQuoted(sql);
        if (!ReadStatement().IsMatch(syntax) || Forbidden().IsMatch(syntax)) { throw Invalid(); }
        return sql;
    }

    internal static string MaskQuoted(string sql)
    {
        var syntax = new StringBuilder(sql.Length);
        for (var index = 0; index < sql.Length; index++)
        {
            var character = sql[index];
            if (character is '\'' or '"')
            {
                var identifier = character == '"';
                var closed = false;
                var quoted = new StringBuilder();
                while (++index < sql.Length)
                {
                    if (sql[index] == '\\') { throw Invalid(); } // Exclude escape and Unicode-string ambiguity.
                    if (sql[index] == character)
                    {
                        if (index + 1 < sql.Length && sql[index + 1] == character)
                        {
                            quoted.Append(character);
                            index++;
                            continue;
                        }
                        closed = true;
                        break;
                    }
                    quoted.Append(sql[index]);
                }
                if (!closed) { throw Invalid(); }
                // Quoted function names must not bypass forbidden-function checks.
                syntax.Append(' ').Append(identifier ? quoted.ToString() : "?").Append(' ');
            }
            else if (character is ';' or '$' or '`' or '\\' ||
                (index + 1 < sql.Length && ((character == '-' && sql[index + 1] == '-') || (character == '/' && sql[index + 1] == '*'))))
            {
                throw Invalid();
            }
            else { syntax.Append(character); }
        }
        return syntax.ToString();
    }

    private static ArgumentException Invalid() => new(Reason, "sql");

    [GeneratedRegex(@"\A\s*(?:SELECT|WITH)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadStatement();

    [GeneratedRegex(@"\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|CREATE|TRUNCATE|GRANT|REVOKE|COPY|CALL|DO|EXECUTE|PREPARE|SET|RESET|INTO|LOCK|COMMIT|ROLLBACK|VACUUM|ANALYZE|LISTEN|NOTIFY)\b|\b(?:set_config|nextval|setval|pg_[a-z_]+|dblink[a-z_]*|lo_[a-z_]+)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Forbidden();
}