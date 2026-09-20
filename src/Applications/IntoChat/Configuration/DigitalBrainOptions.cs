using DigitalBrain.Behaviors;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Time;

namespace DigitalBrain;

public sealed record DigitalBrainOptions : IApplicationConfiguration
{
    public GoogleModuleOptions Google { get; init; } = new();
    public FlutterModuleOptions Flutter { get; init; } = new();

    public static DigitalBrainOptions Web { get; } = new()
    {
        Flutter = new() { Hosting = new() { Kind = FlutterHostKind.Web } },
    };

    public static DigitalBrainOptions Headless { get; } = new()
    {
        Flutter = new() { Hosting = new() { Kind = FlutterHostKind.None } },
    };

    public IReadOnlyList<ModuleDefinition> Modules =>
        [GoogleModule.Define(Google), FlutterModule.Define(Flutter), TimeModule.Define(), new(typeof(TestTwitterModule))];

    public ApplicationConfigurationSnapshot CreateSnapshot()
    {
        var configuration = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in ModuleComposition.Resolve(Modules))
        {
            foreach (var pair in module.Configuration) { configuration.Add(pair.Key, pair.Value); }
        }

        return new("IntoChat", configuration);
    }
}
