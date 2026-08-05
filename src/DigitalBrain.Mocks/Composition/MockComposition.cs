using DigitalBrain.Testing;

namespace DigitalBrain.Mocks;

public static class MockComposition
{
    public static DigitalBrainTestBuilder ComposeMocks(this DigitalBrainTestBuilder brain)
    {
        ArgumentNullException.ThrowIfNull(brain);
        return brain
            .AddModule<MockX>()
            .AddModule<MockGmail>()
            .AddModule<MockCrypto>()
            .AddModule<MockWebSearch>()
            .AddModule<MockSalesforce>();
    }
}
