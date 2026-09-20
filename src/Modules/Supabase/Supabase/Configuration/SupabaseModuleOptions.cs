using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Supabase;

/// <summary>Public module settings. Credentials remain in the host's secret configuration.</summary>
public sealed class SupabaseModuleOptions
{
    public const string SectionName = "DigitalBrain:Supabase";

    public string Provider { get; set; } = SupabaseModule.ProviderName;
    public string ConnectionName { get; set; } = SupabaseModule.ConnectionName;
    public string? ConnectionString { get; internal set; }

    internal void ResolveConnection(IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(ConnectionName))
        {
            ConnectionName = SupabaseModule.ConnectionName;
        }

        ConnectionString = configuration.GetConnectionString(ConnectionName);
    }
}