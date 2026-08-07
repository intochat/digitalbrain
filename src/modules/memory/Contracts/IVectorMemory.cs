using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Memory;

[Description("Owner-isolated vector memory neuron")]
[Alias("DigitalBrain.Memory.IVectorMemory")]
public interface IVectorMemory : INeuron;
