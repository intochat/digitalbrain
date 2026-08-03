using DigitalBrain.Abstractions;

namespace DigitalBrain.AI;

[ClientEntryPoint]
public partial interface ITeam : IAgent
{
    [Alias(nameof(Form))]
    Task Form(TeamFormation formation);
}
