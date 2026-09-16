using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace DigitalBrain.Supabase;

[ModuleHosting("DigitalBrain.Supabase.Aspire.Hosting.SupabaseHosting, DigitalBrain.Modules.Supabase.Aspire.Hosting")]
public sealed class SupabaseModule : IModule
{
    public const string ConnectionName = "supabase";
    public const string ProviderName = "Npgsql";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        if (DigitalBrainFakes.Enabled(builder.Configuration))
        {
            services.TryAddSingleton<FakeSupabaseProvider>();
            services.TryAddSingleton<ISupabaseProvider>(static services => services.GetRequiredService<FakeSupabaseProvider>());
        }
        else
        {
            var connection = builder.Configuration.GetConnectionString(ConnectionName);
            if (string.IsNullOrWhiteSpace(connection))
            {
                throw new InvalidOperationException("Supabase requires ConnectionStrings:supabase. Supply it through the Aspire dashboard or AppHost user secrets.");
            }

            var normalized = SupabaseConnectionSettings.Parse(connection).ConnectionString;
            services.TryAddSingleton(_ => NpgsqlDataSource.Create(normalized));
            services.TryAddSingleton<ISupabaseProvider, SupabaseProvider>();
            services.AddHealthChecks().AddCheck<SupabaseHealthCheck>("supabase", tags: ["ready"]);
        }

        services.AddSingleton<ITableSource>(new TableSource(SupabaseNames.TableIdPrefix, SupabaseNames.TableType));
        services.TryAddSingleton<TableService>();
        services.AddSingleton<SupabaseNativeTools>();
        services.AddNativeTool("supabase_schema", static services => services.GetRequiredService<SupabaseNativeTools>().CreateSchema());
        services.AddNativeTool("supabase_query", static services => services.GetRequiredService<SupabaseNativeTools>().CreateQuery());
        services.AddNativeTool("show_supabase_query_table", static services => services.GetRequiredService<SupabaseNativeTools>().CreateShowQueryTable());
    }
}
