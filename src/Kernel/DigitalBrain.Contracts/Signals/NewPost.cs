namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.new-post")]
public sealed record NewPost : Signal
{
    public NewPost(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Text = text;
    }

    [Id(0)]
    public string Text { get; }
}
