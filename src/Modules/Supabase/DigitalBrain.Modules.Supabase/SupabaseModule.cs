using DigitalBrain.AI.Agents;
using DigitalBrain.Kernel;
using DigitalBrain.Kernel.AspNetCore;
using DigitalBrain.Supabase.Windows;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Supabase;

[ModuleId("supabase")]
public sealed class SupabaseModule : IModule<SupabaseModuleOptions>, IHttpModule
{
    public const string ConnectionName = "supabase";
    public const string ProviderName = "Npgsql";

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        TableEndpoints.Map(endpoints);
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<SupabaseModuleOptions>()
            .Configure<IConfiguration>(static (options, configuration) => configuration.PopulateModuleOptions("supabase", options))
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, ProviderName, StringComparison.OrdinalIgnoreCase),
                "Supabase requires the Npgsql provider.")
            .Validate(static options => IsConnectionValid(options.ConnectionString),
                "Supabase requires a valid PostgreSQL URI or Npgsql connection string for its configured connection name.");
        services.TryAddSingleton(CreateDataSource);
        services.TryAddSingleton<ISupabaseProvider, SupabaseProvider>();
        services.AddLiveTables();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<DigitalBrain.Registry.IRegistryResourceProvider, SupabaseRegistryResources>());
        services.AddSingleton<IAgentToolFactory, LiveTableTools>();
        services.TryAddSingleton<ILiveTableSource>(services => services.GetRequiredService<ISupabaseProvider>());
        services.AddHealthChecks().AddCheck<SupabaseHealthCheck>("supabase", tags: ["ready"]);
    }

    private static bool IsConnectionValid(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection)) { return false; }
        try
        {
            _ = SupabaseConnectionSettings.Parse(connection);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static NpgsqlDataSource CreateDataSource(IServiceProvider services)
    {
        var connection = services.GetRequiredService<IOptions<SupabaseModuleOptions>>().Value.ConnectionString
            ?? throw new InvalidOperationException(
                $"Supabase requires connection string '{ConnectionName}'.");
        return NpgsqlDataSource.Create(SupabaseConnectionSettings.Parse(connection).ConnectionString);
    }
}
