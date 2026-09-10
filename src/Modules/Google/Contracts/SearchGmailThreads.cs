namespace DigitalBrain.Google;

[GenerateSerializer]
[Alias("db.gmail.search-threads")]
public sealed record SearchGmailThreads(
    [property: Id(0)] string Query,
    [property: Id(1)] int PageSize = 10,
    [property: Id(2)] string? PageToken = null);
