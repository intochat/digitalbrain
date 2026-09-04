namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.publish-post")]
public sealed record PublishPost : Signal
{
    public PublishPost(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
    }

    [Id(0)]
    public string Text { get; }
}
