using DigitalBrain.Testing;

namespace DigitalBrain.Mocks;

// Stage-1 mock platform: the four integration stand-ins scenario tests compose by name.
// No network, no product modules — journal-visible neurons only.
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
