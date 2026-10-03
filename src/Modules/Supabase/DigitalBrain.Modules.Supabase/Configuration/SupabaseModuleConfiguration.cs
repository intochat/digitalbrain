using DigitalBrain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Supabase;

public static class SupabaseModuleConfiguration
{
    public static ModuleConfiguration<SupabaseModule> WithProvider<TProvider>(this ModuleConfiguration<SupabaseModule> module)
        where TProvider : class, ISupabaseProvider
    {
        module.ConfigureLocalServices(services => services.Replace(ServiceDescriptor.Singleton<ISupabaseProvider, TProvider>()));
        return module;
    }
}
