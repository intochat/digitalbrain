namespace MODULE_NAMESPACE.Tests.Integration;

public sealed class MODULE_CLASSModuleIntegrationFacts
{
    [Fact]
    public void ModuleLoadsSuccessfully()
    {
        var module = new MODULE_CLASSModule();
        Assert.NotNull(module);
    }
}
