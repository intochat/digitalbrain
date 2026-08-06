using DigitalBrain.Testing;

namespace DigitalBrain.Mocks.Tests.Support;

internal static class MockComposition
{
    internal static DigitalBrainTestBuilder ComposeMocks(this DigitalBrainTestBuilder composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return composition
            .RegisterVocabulary(typeof(MockX).Assembly)
            .RegisterNeuron<MockX>("mockx")
            .RegisterNeuron<MockGmail>("mockgmail")
            .RegisterNeuron<MockCrypto>("mockcrypto")
            .RegisterNeuron<MockWebSearch>("mockwebsearch")
            .RegisterNeuron<MockSalesforce>("mocksalesforce");
    }
}
