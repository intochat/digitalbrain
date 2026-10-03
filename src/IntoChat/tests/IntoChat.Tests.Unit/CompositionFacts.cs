extern alias AppHost;

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
    }
}
