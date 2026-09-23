namespace DigitalBrain.Automations;

[GenerateSerializer, Alias("automations.state")]
internal sealed class AutomationState
{
    [Id(0)] public AutomationDefinition? Definition { get; set; }
    [Id(1)] public bool Active { get; set; }
    [Id(2)] public decimal SpentCompute { get; set; }
    [Id(3)] public Dictionary<long, JobRun> Runs { get; set; } = [];
}
