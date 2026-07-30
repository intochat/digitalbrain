using DigitalBrain.Abstractions;

namespace DigitalBrain.TestingTests.Harness;

public partial interface IGreeter : INeuron
{
    [Alias(nameof(Greet))]
    Task Greet(string name);
}
