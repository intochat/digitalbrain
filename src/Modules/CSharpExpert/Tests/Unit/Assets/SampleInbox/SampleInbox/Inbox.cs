using System.Collections.Generic;

namespace SampleInbox;

public sealed class Inbox
{
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> Messages => _messages;

    public void Add(string message) => _messages.Add(message);
}
