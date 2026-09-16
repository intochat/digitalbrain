using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Supabase.Aspire.Hosting;

/// <summary>Default hosting for AddModule&lt;SupabaseModule&gt;().</summary>
public sealed class SupabaseHosting : IDigitalBrainModuleHosting
{
    public void Configure(DigitalBrainBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        GetState(brain);
    }

    internal static void ConfigureOptions(DigitalBrainBuilder brain, SupabaseHostingOptions options)
        => GetState(brain).Configure(options);

    private static SupabaseProjection GetState(DigitalBrainBuilder brain)
    {
        var state = brain.GetOrAddState(static owner => new SupabaseProjection(owner), out var added);
        if (added) { brain.AddProjection(state); }
        return state;
    }

    private sealed class SupabaseProjection(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<IResourceWithConnectionString>? _connection;
        private SupabaseHostingOptions _options = new();

        internal void Configure(SupabaseHostingOptions options)
        {
            if (_connection is not null)
            {
                throw new InvalidOperationException("Configure Supabase hosting before projecting it to an application.");
            }

            _options = options;
        }

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            _connection ??= brain.ApplicationBuilder
                .AddConnectionString(_options.ParameterName)
                .WithParentRelationship(brain.Resource);
            builder.WithReference(_connection, connectionName: _options.ConnectionName)
                .WithEnvironment("DigitalBrain__Supabase__Provider", SupabaseModule.ProviderName)
                .WithEnvironment("DigitalBrain__Supabase__ConnectionName", _options.ConnectionName);
        }
    }
}
