using System.Globalization;
using Npgsql;

namespace DigitalBrain.Supabase;

internal static class SupabaseConnectionSettings
{
    public static NpgsqlConnectionStringBuilder Parse(string connection)
    {
        try
        {
            var settings = connection.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
                connection.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase)
                ? FromUri(new Uri(connection)) : new NpgsqlConnectionStringBuilder(connection);
            if (string.IsNullOrWhiteSpace(settings.Host)) { throw new ArgumentException("Host is required."); }
            settings.IncludeErrorDetail = false;
            settings.LogParameters = false;
            settings.PersistSecurityInfo = false;
            settings.CommandTimeout = 15;
            settings.Timeout = 10;
            settings.MaxAutoPrepare = 0;
            return settings;
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        {
            // Do not retain the driver exception: its message can contain the supplied secret.
            throw new InvalidOperationException("ConnectionStrings:supabase must be a PostgreSQL URI or an Npgsql connection string (Host=...;Database=...;Username=...;Password=...).");
        }
    }

    private static NpgsqlConnectionStringBuilder FromUri(Uri uri)
    {
        var credentials = uri.UserInfo.Split(':', 2);
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port < 0 ? 5432 : uri.Port,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(credentials[0]),
            Password = credentials.Length == 2 ? Uri.UnescapeDataString(credentials[1]) : null,
            SslMode = SslMode.Require,
        };
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2) { throw new FormatException(); }
            var key = Uri.UnescapeDataString(pair[0]);
            var value = Uri.UnescapeDataString(pair[1]);
            switch (key.ToLowerInvariant())
            {
                case "sslmode":
                    settings.SslMode = Enum.Parse<SslMode>(value.Replace("-", "", StringComparison.Ordinal), ignoreCase: true);
                    break;
                case "connect_timeout":
                    settings.Timeout = int.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "application_name":
                    settings.ApplicationName = value;
                    break;
                case "options":
                    settings.Options = value;
                    break;
                default:
                    settings[key] = value;
                    break;
            }
        }
        return settings;
    }
}