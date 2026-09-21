using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Supabase;

public sealed class SupabaseConfigurationContract() : ModuleConfigurationContract<SupabaseModule, SupabaseModuleOptions>(
    "Provider", "ConnectionName", "Hosting.Kind")
{
    protected override ModuleDefinition Compile(SupabaseModuleOptions options) => SupabaseModule.Define(options);
}

public static class SupabaseModuleConfiguration
{
    public static ModuleConfiguration<SupabaseModule> WithProvider<TProvider>(this ModuleConfiguration<SupabaseModule> module)
        where TProvider : class, ISupabaseProvider
    {
        module.ConfigureLocalServices(services => services.Replace(ServiceDescriptor.Singleton<ISupabaseProvider, TProvider>()));
        return module;
    }

    public static ModuleConfiguration<SupabaseModule> WithConnection(this ModuleConfiguration<SupabaseModule> module, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        module.ConfigureOptions<SupabaseModuleOptions>(o =>
        {
            o.ConnectionName = name;
            o.Hosting.Kind = SupabaseHostKind.External;
        }, "ConnectionName", "Hosting.Kind");
        return module;
    }

    public static ModuleConfiguration<SupabaseModule> WithPostgres(this ModuleConfiguration<SupabaseModule> module)
    {
        module.ConfigureOptions<SupabaseModuleOptions>(o => o.Hosting.Kind = SupabaseHostKind.Postgres, "Hosting.Kind");
        return module;
    }
}