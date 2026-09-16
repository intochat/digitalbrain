using DigitalBrain.Aspire.Hosting;

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
        SupabaseHosting.ConfigureOptions(module.DigitalBrainBuilder, options);
        return module;
    }
}
