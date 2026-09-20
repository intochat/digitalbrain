using DigitalBrain.Behaviors;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Time;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain;

public sealed record DigitalBrainConfiguration : IApplicationConfiguration
{
    public const string SectionName = "DigitalBrain";

    public GoogleModuleOptions Google { get; init; } = new();
    public FlutterModuleOptions Flutter { get; init; } = new();

    public IReadOnlyList<ModuleDefinition> Modules =>
        [GoogleModule.Define(Google), FlutterModule.Define(Flutter), TimeModule.Define(), new(typeof(TestTwitterModule))];

    public static DigitalBrainConfiguration Bind(IConfiguration configuration) => new()
    {
        Google = configuration.GetSection(GoogleModule.GmailOAuthConfigurationRoot).Get<GoogleModuleOptions>() ?? new(),
        Flutter = configuration.GetSection("DigitalBrain:Flutter").Get<FlutterModuleOptions>() ?? new(),
    };
}
