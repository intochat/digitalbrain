using DigitalBrain.Apps;

namespace DigitalBrain.Testing.Unit;

public static class ApplicationTestBuilder
{
    public static UnitTestBuilder WithApp<TApp>(this UnitTestBuilder builder) where TApp : IApplication, new()
    {
        ArgumentNullException.ThrowIfNull(builder);
        var application = AppDefinition.Of<TApp>();
        builder.RequireModules(application.RequiredModules);
        return builder.ConfigureSilo(silo => silo.AddApplication(application));
    }
}
