using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Supabase;

public sealed class SupabaseOptions
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
