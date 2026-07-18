using DigitalBrain.V2.Core.Runtime;

namespace Greeter.Contracts;

public interface IGreeterNeuron : INeuron, IHandle<Hello>, IEmit<Announce>;

public interface IRoomNeuron : INeuron, IHandle<Announce>, IEmit<Announced>;

public interface IBystanderNeuron : INeuron, IHandle<Hello>, IEmit<BystanderHeardHello>;
