using DigitalBrain.AI.Agents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace DigitalBrain.Supabase.Windows;

// Shared live-table infrastructure. Each window binds to its creation source; adding another
// PostgreSQL connection does not replace the Supabase connection or duplicate the table engine.
public static class LiveTableHosting
{
    public static IServiceCollection AddLiveTables(this IServiceCollection services)
    {
        services.TryAddSingleton<LiveTableWindows>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAgentToolFactory, TableViewTools>());
        return services;
    }

    public static ILiveTableSource CreatePostgresSource(NpgsqlDataSource source) => new SupabaseProvider(source);
}
