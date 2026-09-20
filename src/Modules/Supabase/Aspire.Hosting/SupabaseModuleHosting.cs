using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Supabase.Aspire.Hosting;

public sealed class SupabaseModuleHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        var options = brain.GetModuleConfiguration<SupabaseModule>().GetSection(SupabaseModuleOptions.SectionName)
            .Get<SupabaseModuleOptions>() ?? new();
        var module = new DigitalBrainModuleBuilder<SupabaseModule>(brain);
        if (options.Hosting.Kind == SupabaseHostKind.External)
        {
            module.WithSupabase(o => { o.ParameterName = options.ConnectionName; o.ConnectionName = options.ConnectionName; });
            return;
        }
        var server = brain.ApplicationBuilder.AddPostgres("supabase-postgres").WithParentRelationship(module.Resource);
        var database = server.AddDatabase("supabase-database", options.ConnectionName);
        brain.AddProjection(new PostgresProjection(database, options.ConnectionName));
    }

    private sealed class PostgresProjection(IResourceBuilder<PostgresDatabaseResource> database, string connectionName)
        : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
            => builder.WithReference(database, connectionName)
                .WithAnnotation(new WaitAnnotation(database.Resource, WaitType.WaitUntilHealthy, exitCode: 0));
    }
}
