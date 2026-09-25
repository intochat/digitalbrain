using SampleInbox;
using Xunit;

namespace SampleInbox.Tests;

public sealed class InboxTests
{
    [Fact]
    public void AddKeepsMessages()
    {
        var inbox = new Inbox();
        inbox.Add("hello");
        Assert.Single(inbox.Messages);
    }
}
