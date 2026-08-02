using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[Alias("introspection")]
[Description("Owner-scoped neuron reporting the brain's own journals, tallies and topology")]
public partial interface IIntrospection : INeuron;
