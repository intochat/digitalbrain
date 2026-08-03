using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[Alias("introspection")]
[Description("Introspection neuron reporting journal tallies, journaled facts and runtime topology for the owning identity")]
public partial interface IIntrospection : INeuron;
