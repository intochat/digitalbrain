using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Orleans.Hosting;

namespace DigitalBrain.Supabase;

public sealed class SupabaseModule : IModule
{
    public const string ConnectionName = "supabase";
    public const string ProviderName = "Npgsql";

    public static ModuleDefinition Define(SupabaseModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(SupabaseModule), new Dictionary<string, string?>
        {
            [SupabaseModuleOptions.SectionName + ":Provider"] = options.Provider,
            [SupabaseModuleOptions.SectionName + ":ConnectionName"] = options.ConnectionName,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<SupabaseModuleOptions>()
            .Bind(builder.Configuration.GetSection(SupabaseModuleOptions.SectionName))
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, ProviderName, StringComparison.OrdinalIgnoreCase),
                "Supabase requires the Npgsql provider.")
            .Validate(static options => IsConnectionValid(options.ConnectionString),
                "Supabase requires a valid PostgreSQL URI or Npgsql connection string for its configured connection name.");
        services.TryAddSingleton(CreateDataSource);
        services.TryAddSingleton<ISupabaseProvider, SupabaseProvider>();
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