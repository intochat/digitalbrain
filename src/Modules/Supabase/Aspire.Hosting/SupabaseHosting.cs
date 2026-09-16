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
        var state = brain.GetOrAddState(static owner => new SupabaseProjection(owner), out var added);
        if (added) { brain.AddProjection(state); }
    }

    private sealed class SupabaseProjection(DigitalBrainBuilder brain) : DigitalBrainModuleProjection
    {
        private IResourceBuilder<IResourceWithConnectionString>? _connection;

        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            // Delay parameter creation until projection, so fake mode works in either fluent order.
            if (brain.FakesEnabled) { return; }

            _connection ??= brain.ApplicationBuilder.AddConnectionString(SupabaseModule.ConnectionName);
            builder.WithReference(_connection)
                .WithEnvironment("DigitalBrain__Supabase__Provider", SupabaseModule.ProviderName);
        }
    }
}
