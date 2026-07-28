using DigitalBrain.Chat;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Aspire.Hosting;
using DigitalBrain.ServiceDefaults;
using DigitalBrain.Testing;

namespace DigitalBrain.UI.Tests;

public sealed class UIFixture : DigitalBrainFixture
{
    public const string DefaultShellName = FlutterHostingExtensions.DefaultShellName;

    public const string DefaultUIResourceName = FlutterHostingExtensions.DefaultUIResourceName;

    public const string UIBaseEnvironmentVariable = FlutterHostingExtensions.UIBaseEnvironmentVariable;

    public static Uri ResolveProductUIBaseAddress()
    {
        var configured = Environment.GetEnvironmentVariable(UIBaseEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            return LaunchSettingsUIBase;
        }

        return new Uri(configured.TrimEnd('/') + "/");
    }

    public static async Task<WebApplication> StartUIEdgeAsync(
        TestBrain test,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(test);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DigitalBrain:Modules:0"] = FlutterModule.Id.Value,
                ["DigitalBrain:Modules:1"] = ChatModule.Id.Value,
            });
        builder.Services.AddSingleton(test.Client);
        builder.Services.AddSingleton<IGrainFactory>(test.Cluster.Client);
        builder.Services.AddUIEdgeServices();

        var app = builder.Build();
        app.MapDefaultEndpoints();
        app.MapUIHost();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private static readonly Uri LaunchSettingsUIBase = new("http://localhost:5080/");

    protected override void Configure(DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        brain.AddModule<FlutterModule>();
        brain.AddModule<ChatModule>();
    }
}
