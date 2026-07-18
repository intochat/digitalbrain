using DigitalBrain.Core.Runtime;

namespace DigitalBrain.Abstractions.Ino;

public interface IConsole : INeuron
{
    // Mirrors NeuronNaming.ToGrainType(typeof(Console)); the kernel client routes console input
    // by this contract without referencing the Ino bundle's Console neuron implementation.
    const string GrainType = "console";
}
