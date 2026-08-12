using DigitalBrain.AI;
using DigitalBrain.AI.Aspire.Hosting;
using DigitalBrain.AI.Ollama;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Memory;
using DigitalBrain.Memory.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using DigitalBrain.UI;
using DigitalBrain.UI.Aspire.Hosting;
using Microsoft.Extensions.Hosting;

// AppHost half of the product catalog (Aspire projections).
// Silo contracts/implementations: Kernel/ProductModules.Assemblies.
// When adding a product surface that needs AppHost resources, update BOTH:
//   1) ProductModules.Assemblies (+ AspireProjectedModuleNames)
//   2) AddModule below
internal static class ProductComposition
{
    // Must match ProductModules.AspireProjectedModuleNames (Kernel).
    private static readonly string[] AspireProjectedModuleNames =
    [
        "DigitalBrain.AI.AIModule",
        "DigitalBrain.Memory.MemoryModule",
        "DigitalBrain.UI.UiModule",
        "DigitalBrain.Google.GoogleModule",
        "DigitalBrain.Salesforce.SalesforceModule",
    ];

    public static DigitalBrainBuilder AddProductModules(
        this DigitalBrainBuilder brain,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(brain);
        ArgumentNullException.ThrowIfNull(environment);

        Type[] registered =
        [
            typeof(AIModule),
            typeof(MemoryModule),
            typeof(UiModule),
            typeof(GoogleModule),
            typeof(SalesforceModule),
        ];

        brain.AddModule<AIModule>(ai =>
        {
            ai.EnableSensitiveData = environment.IsDevelopment();
            ai.WithLlm<Gemma4>();
        });
        brain.AddModule<MemoryModule>(memory => memory.WithQdrant());
        brain.AddModule<UiModule>(ui => ui.WithWindowHost());
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        EnsureCatalog(registered);
        return brain;
    }

    private static void EnsureCatalog(Type[] registered)
    {
        var expected = new HashSet<string>(AspireProjectedModuleNames, StringComparer.Ordinal);
        foreach (var module in registered)
        {
            var name = module.FullName
                ?? throw new InvalidOperationException($"Module type '{module.Name}' has no full name.");
            if (!expected.Remove(name))
            {
                throw new InvalidOperationException(
                    $"AppHost registers {name} but ProductComposition.AspireProjectedModuleNames does not list it. "
                    + "Update ProductComposition and Kernel ProductModules together.");
            }
        }

        if (expected.Count > 0)
        {
            throw new InvalidOperationException(
                $"ProductComposition catalog lists [{string.Join(", ", expected)}] without AddModule. "
                + "Update AddProductModules.");
        }
    }
}
