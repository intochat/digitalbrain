using DigitalBrain.Core;
using DigitalBrain.Apps;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Apps.Tests.Unit;

public sealed class AppRegistrationFacts
{
    [Fact]
    public void AppsRequireHostModulesAndCompositionFreezesRegistration()
    {
        Assert.Throws<InvalidOperationException>(() => new BrainCompositionBuilder().AddApp<ExampleApp>().Build());
        var builder = new BrainCompositionBuilder().WithModule<AppsModule>().AddApp<ExampleApp>();
        var composition = builder.Build();
        Assert.Equal("test.example", Assert.Single(composition.Apps).Definition.Id);
        Assert.Throws<InvalidOperationException>(builder.AddApp<ExampleApp>);
        Assert.Throws<InvalidOperationException>(() => new BrainCompositionBuilder().AddApp<ExampleApp>().AddApp<ExampleApp>());
    }

    [Fact]
    public void AppsCanBeDeclaredInsideTheAppsModuleConfiguration()
    {
        var builder = new BrainCompositionBuilder().WithModule<AppsModule>(apps => apps
            .AddApp<ExampleApp>()
            .AddApp<SecondExampleApp>());

        var composition = builder.Build();

        Assert.Equal(["test.example", "test.second"], composition.Apps.Select(app => app.Definition.Id));
        Assert.Throws<InvalidOperationException>(() => builder.ConfigureModule<AppsModule>(apps => apps.AddApp<ExampleApp>()));
    }
}

public sealed class ExampleApp(IPersistentState<ExampleState> state) : App<ExampleState>(state), IAppDefinition
{
    public static AppDefinition Definition => new("test.example", [typeof(AppsModule)]);
}
public sealed class ExampleState;

public sealed class SecondExampleApp(IPersistentState<ExampleState> state) : App<ExampleState>(state), IAppDefinition
{
    public static AppDefinition Definition => new("test.second", [typeof(AppsModule)]);
}
