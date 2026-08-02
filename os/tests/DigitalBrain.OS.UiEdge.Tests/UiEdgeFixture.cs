using DigitalBrain.AI;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Runtime;
using DigitalBrain.Chat;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Shell;
using DigitalBrain.Shell.Aspire.Hosting;
using DigitalBrain.Tasks;
using DigitalBrain.Testing;

namespace DigitalBrain.OS.UiEdge.Tests;

public sealed class UiEdgeFixture : DigitalBrainFixture
{
    public const string DefaultShellName = ShellHostingExtensions.DefaultShellName;

    public const string DefaultUIResourceName = ShellHostingExtensions.DefaultUIResourceName;

    public const string UIBaseEnvironmentVariable = ShellHostingExtensions.UIBaseEnvironmentVariable;

    public static Uri ResolveProductUIBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable(UIBaseEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return LaunchSettingsUIBase;
        }

        return new Uri(configured.TrimEnd('/') + "/");
    }

    public static async Task<WebApplication> StartUiHttpAsync(
        TestBrain test,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null)
    {
        ArgumentNullException.ThrowIfNull(test);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DigitalBrain:Modules:0"] = ShellModule.Id.Value,
                ["DigitalBrain:Modules:1"] = ChatModule.Id.Value,
                ["DigitalBrain:Modules:2"] = BehaviorsModule.Id.Value,
                ["DigitalBrain:Modules:3"] = TasksModule.Id.Value,
            });
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<IGrainFactory>(test.Cluster.Client);
        builder.Services.AddUiEdgeServices();
        // L1 UI host has no live model: inject a fixed author so scenario approval still proposes.
        builder.Services.AddSingleton<IBehaviorAuthor>(_ => new BehaviorAuthor(
            static (_, _) => Task.FromResult(AccountEnrichmentTestProgram.ProgramSource)));
        configureServices?.Invoke(builder.Services);

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapDefaultEndpoints();
        app.MapUiEdgeHost();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static readonly Uri LaunchSettingsUIBase = new("http://localhost:5080/");

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<ShellModule>();
        brain.AddModule<ChatModule>();
        brain.AddModule<AIModule>();
        brain.AddModule<BehaviorsModule>();
        brain.AddModule<TasksModule>();
    }
}
