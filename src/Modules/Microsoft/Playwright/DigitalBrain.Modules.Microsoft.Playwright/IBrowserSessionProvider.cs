namespace DigitalBrain.Microsoft.Playwright;

public interface IBrowserSessionProvider
{
    Task<IBrowserPageSession> AttachAsync(BrowserAttachment attachment, CancellationToken ct);
}
public interface IBrowserPageSession : IAsyncDisposable
{
    bool Connected { get; }
    string? Url { get; }
    string? Title { get; }
    Task<BrowserObservation> NavigateAsync(string url, CancellationToken ct);
    Task<BrowserObservation> SnapshotAsync(CancellationToken ct);
    Task<BrowserObservation> ClickAsync(string selector, CancellationToken ct);
    Task<BrowserObservation> FillAsync(string selector, string value, CancellationToken ct);
}
