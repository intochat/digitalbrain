extern alias AppHost;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Testing.E2E;

namespace IntoChat.Tests.Unit;

// Builds the real AppHost model without starting any resource. The CSharp module owns
// compilation and authoring; the product uses the same connector set as the reference composition.
public sealed class CompositionFacts
{
    [Fact]
    public async Task ProductMatchesTheReferenceCompositionModuleSet()
    {
        await using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<AppHost::Projects.IntoChat_AppHost>(
            ["Parameters:Modules-digitalbrain-master-key=synthetic-composition-test-key-32chars"], TestContext.Current.CancellationToken);
        var productModules = appHost.Resources.SelectMany(resource => resource.Annotations.OfType<BrainModuleAnnotation>())
            .Select(module => module.ModuleId).Order().ToArray();
        var referenceModules = ReferenceBrain.Create().BuildComposition().Modules
            .Select(module => DigitalBrain.Kernel.ModuleIdentity.Get(module.ModuleType)).Order().ToArray();
        Assert.Equal(referenceModules, productModules);
        Assert.Contains("qdrant", productModules);
        Assert.DoesNotContain("memory", productModules);
        Assert.Contains(appHost.Resources, resource => resource.Name == "Modules-openrouter-api-key"
            && resource is ParameterResource { Secret: true });
        var runtime = appHost.Resources.Single(resource => resource.Name == "IntoChat");
        var environment = new Dictionary<string, object>();
        var context = new EnvironmentCallbackContext(appHost.ExecutionContext, runtime, environment, TestContext.Current.CancellationToken);
        foreach (var callback in runtime.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await callback.Callback(context);
        }
        Assert.Equal("IDeepSeekV41Flash", environment["DigitalBrain__Modules__ai__Options__Default__Model"]);
        Assert.True(environment.ContainsKey("DigitalBrain__Integrations__openrouter__ApiKey"));
    }
}
