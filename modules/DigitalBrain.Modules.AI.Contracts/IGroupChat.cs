using DigitalBrain.Tasks;

namespace DigitalBrain.AI;

[Alias("ai.group-chat")]
public interface IGroupChat : IAgent, IWorker;
