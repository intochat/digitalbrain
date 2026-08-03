using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Google;

[Description("Owner-scoped Gmail neuron identified by module-owned connection name")]
public partial interface IGmail : INeuron;
