using Xunit;

namespace DigitalBrain.Aspire.Tests;

[Collection(ModelCollection.Name)]
public sealed class PersonaPlexHostingTests(ModelFixture fixture)
{
    [Fact]
    public async Task KernelRenderedEnvironmentContainsPersonaPlexEnabledSetting()
    {
        var environment = await fixture.Model.RenderedEnvironmentAsync(ProductSurfaceResourceNames.Kernel);

        Assert.True(environment.ContainsKey("DigitalBrain__AI__PersonaPlex__Enabled"));
    }
}
