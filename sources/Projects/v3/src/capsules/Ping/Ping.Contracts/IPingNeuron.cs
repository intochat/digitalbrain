using DigitalBrain.V2.Core.Runtime;

namespace Ping.Contracts;

// THE MANIFEST. Pure metadata, zero logic. Scanning this interface (without loading the
// implementation or running anything) tells the OS: this neuron consumes Ping and emits Pong.
//   IHandle<Ping> = in-edge,  IEmit<Pong> = out-edge.
// The constellation graph and the closed-loop Architect are built from edges like these.
public interface IPingNeuron : INeuron, IHandle<Ping>, IEmit<Pong>;
