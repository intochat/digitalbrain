using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Supabase.Aspire.Hosting;

public static class SupabaseHostingExtensions
{
    public static DigitalBrainModuleBuilder<SupabaseModule> WithSupabase(
        this DigitalBrainModuleBuilder<SupabaseModule> module,
        Action<SupabaseHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new SupabaseHostingOptions();
        configure?.Invoke(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ParameterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionName);
        State(module).Enable(options);
        return module;
    }

    private static SupabaseHostingState State(DigitalBrainModuleBuilder<SupabaseModule> module)
    {
        var state = module.DigitalBrainBuilder.GetOrAddState(brain => new SupabaseHostingState(brain, module.Resource), out var added);
        if (added)
        {
            module.AddProjection(state);
        }

        return state;
    }

    private sealed class SupabaseHostingState(
        DigitalBrainBuilder brain,
        IResourceBuilder<DigitalBrainModuleResource> module) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<ParameterResource>? _connection;
        private SupabaseHostingOptions _options = new();

        internal void Enable(SupabaseHostingOptions options)
        {
            if (_connection is not null)
            {
                throw new InvalidOperationException("Configure Supabase hosting before projecting it to an application.");
            }

            _options = options;
            _connection = brain.ApplicationBuilder
                .AddParameter("supabase-connection", () => brain.ApplicationBuilder.Configuration["Parameters:supabase-connection"]
                    ?? brain.ApplicationBuilder.Configuration.GetConnectionString(_options.ParameterName)
                    ?? throw new InvalidOperationException($"Connection string '{_options.ParameterName}' is required."), secret: true)
                .WithParentRelationship(module);
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            if (_connection is null)
            {
                return;
            }

            builder.WithEnvironment($"ConnectionStrings__{_options.ConnectionName}", _connection)
                .WithEnvironment("DigitalBrain__Supabase__Provider", SupabaseModule.ProviderName)
                .WithEnvironment("DigitalBrain__Supabase__ConnectionName", _options.ConnectionName);
        }
    }
}