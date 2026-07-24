using DigitalBrain.Tasks;

namespace DigitalBrain.AI;

[Alias("ai.group-chat")]
public partial interface IGroupChat : IAgent, IWorker;
