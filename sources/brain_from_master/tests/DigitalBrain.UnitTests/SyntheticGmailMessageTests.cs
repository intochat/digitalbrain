using DigitalBrain.FeatureHost;
using Xunit;
namespace DigitalBrain.UnitTests;

public sealed class SyntheticGmailMessageTests
{
    [Fact]
    public void Reserved_demo_message_is_clearly_labelled_and_uses_the_gmail_contract()
    {
        Assert.True(SyntheticGmailMessages.TryRead("synthetic-demo-priya-northstar", out var message));
        Assert.Equal("synthetic-demo-priya-northstar", message.MessageId);
        Assert.Equal("priya.natarajan@northstarrobotics.example", message.SenderAddress);
        Assert.Contains("Synthetic demo message", message.Subject, StringComparison.Ordinal);
        Assert.Contains("Priya Natarajan", message.PlainTextBody, StringComparison.Ordinal);
        Assert.Contains("pilot rollout", message.PlainTextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_message_ids_are_not_intercepted()
    {
        Assert.False(SyntheticGmailMessages.TryRead("provider-message-1", out _));
    }
}
