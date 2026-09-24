using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Core;

// Transports frozen app declarations through the same module configuration as production and test hosts.
public sealed class AppCompositionModule : IModule
{
    public void Configure(Orleans.Hosting.ISiloBuilder silo)
    {
        foreach (var name in silo.Configuration.GetSection("DigitalBrain:Apps").Get<string[]>() ?? [])
        {
            var type = Type.GetType(name, throwOnError: true)!;
            if (!typeof(Neuron).IsAssignableFrom(type) || !typeof(IAppDefinition).IsAssignableFrom(type) || type.IsAbstract)
            { throw new InvalidOperationException("App registrations must be concrete app neurons."); }
            var definition = type.GetProperty("Definition", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) as AppDefinition
                ?? throw new InvalidOperationException("An app must expose its static Definition.");
            silo.Services.AddSingleton(new AppRegistration(type, definition));
        }
    }
}
