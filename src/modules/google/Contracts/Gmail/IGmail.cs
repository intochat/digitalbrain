using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[Description("Owner-scoped Gmail neuron identified by module-owned connection name")]
[Alias("DigitalBrain.Google.IGmail")]
public interface IGmail : INeuron;
