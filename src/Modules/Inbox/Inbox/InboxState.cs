namespace DigitalBrain.Inbox;

[GenerateSerializer, Alias("inbox.state")]
public sealed class InboxState
{
    [Id(0)] public List<InboxItem> Items { get; set; } = [];
}
