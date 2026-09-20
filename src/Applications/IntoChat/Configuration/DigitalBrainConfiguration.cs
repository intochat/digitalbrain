using DigitalBrain.Behaviors;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Google;
using DigitalBrain.Time;

namespace DigitalBrain;

public sealed record DigitalBrainConfiguration
{
    public const string SectionName = "DigitalBrain";

    public GoogleModuleOptions Google { get; init; } = new();
    public FlutterModuleOptions Flutter { get; init; } = new();

    public IReadOnlyList<ModuleDefinition> Modules =>
        [GoogleModule.Define(Google), FlutterModule.Define(Flutter), TimeModule.Define(), new(typeof(TestTwitterModule))];
}
