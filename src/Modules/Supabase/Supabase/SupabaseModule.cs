using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DigitalBrain.Supabase;

[ModuleHosting("DigitalBrain.Supabase.Aspire.Hosting.SupabaseHosting, DigitalBrain.Modules.Supabase.Aspire.Hosting")]
public sealed class SupabaseModule : IModule
{
    public const string ConfigurationRoot = SupabaseOptions.SectionName;
    public const string ConnectionName = "supabase";
    public const string ProviderName = "Npgsql";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<SupabaseOptions>()
            .BindConfiguration(ConfigurationRoot)
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, ProviderName, StringComparison.OrdinalIgnoreCase),
                "Supabase requires the Npgsql provider.")
            .Validate(static options =>
            {
                if (string.IsNullOrWhiteSpace(options.ConnectionString)) { return false; }
                try
                {
                    _ = SupabaseConnectionSettings.Parse(options.ConnectionString);
                    return true;
                }
                catch (InvalidOperationException) { return false; }
            }, "Supabase requires a valid PostgreSQL URI or Npgsql connection string for its configured connection name.")
            .ValidateOnStart();
        services.TryAddSingleton(services => NpgsqlDataSource.Create(SupabaseConnectionSettings.Parse(
            services.GetRequiredService<IOptions<SupabaseOptions>>().Value.ConnectionString!).ConnectionString));
        services.TryAddSingleton<ISupabaseProvider, SupabaseProvider>();
        services.AddHealthChecks().AddCheck<SupabaseHealthCheck>("supabase", tags: ["ready"]);

        services.AddSingleton<ITableSource>(new TableSource(SupabaseNames.TableIdPrefix, SupabaseNames.TableType));
        services.TryAddSingleton<TableService>();
        services.AddSingleton<SupabaseNativeTools>();
        services.AddNativeTool("supabase_schema", static services => services.GetRequiredService<SupabaseNativeTools>().CreateSchema());
        services.AddNativeTool("supabase_query", static services => services.GetRequiredService<SupabaseNativeTools>().CreateQuery());
        services.AddNativeTool("show_supabase_query_table", static services => services.GetRequiredService<SupabaseNativeTools>().CreateShowQueryTable());
    }
}
