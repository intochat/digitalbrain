using System.Text.Json.Serialization;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Supabase;

// Public module settings. Credentials remain in the host's secret configuration.
public sealed class SupabaseModuleOptions : IModuleOptions
{
    public string Provider { get; set; } = SupabaseModule.ProviderName;
    public string ConnectionName { get; set; } = SupabaseModule.ConnectionName;
    public SupabaseResourceOptions Hosting { get; set; } = new();
    [JsonIgnore]
    public string? ConnectionString { get; internal set; }

    public SupabaseModuleOptions WithConnection(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ConnectionName = name;
        Hosting.Kind = SupabaseHostKind.External;
        return this;
    }

    public SupabaseModuleOptions WithPostgres()
    {
        Hosting.Kind = SupabaseHostKind.Postgres;
        return this;
    }

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ConnectionName);
        if (!Enum.IsDefined(Hosting.Kind)) { throw new ArgumentOutOfRangeException(nameof(Hosting), "Unknown Supabase host kind."); }
    }

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            ConnectionName = SupabaseModule.ConnectionName;
        }

        ConnectionString = configuration.GetConnectionString(ConnectionName);
    }
}

public enum SupabaseHostKind { External, Postgres }

public sealed class SupabaseResourceOptions
{
    public SupabaseHostKind Kind { get; set; }
}
