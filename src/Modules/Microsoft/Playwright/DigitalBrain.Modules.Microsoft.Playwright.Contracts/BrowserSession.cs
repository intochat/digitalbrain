namespace DigitalBrain.Microsoft.Playwright;

[GenerateSerializer, Alias("playwright.attachment")]
public sealed record BrowserAttachment([property: Id(0)] int Port, [property: Id(1)] string SessionId);
[GenerateSerializer, Alias("playwright.session")]
public sealed record BrowserSession([property: Id(0)] bool Connected, [property: Id(1)] string? SessionId,
    [property: Id(2)] string? Url, [property: Id(3)] string? Title);
[GenerateSerializer, Alias("playwright.observation")]
public sealed record BrowserObservation([property: Id(0)] string Url, [property: Id(1)] string Title,
    [property: Id(2)] string Text, [property: Id(3)] IReadOnlyList<BrowserLink> Links);
[GenerateSerializer, Alias("playwright.link")]
public sealed record BrowserLink([property: Id(0)] string Text, [property: Id(1)] string Url);
