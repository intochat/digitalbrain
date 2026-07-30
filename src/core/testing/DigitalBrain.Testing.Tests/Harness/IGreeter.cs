using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

[Alias("harness.greeter")]
[Description("Harness greeter neuron")]
public partial interface IGreeter : INeuron
{
    [Alias(nameof(Greet))]
    Task Greet(string name);
}
