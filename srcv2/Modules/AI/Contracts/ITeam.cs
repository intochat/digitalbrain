using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[ClientEntryPoint]
[Alias("DigitalBrain.AI.ITeam")]
public interface ITeam : IAgent
{
    [Alias(nameof(Form))]
    Task Form(TeamFormation formation);
}
