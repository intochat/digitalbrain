using DigitalBrain.Contracts;
namespace DigitalBrain.Microsoft.Playwright;

[Alias("playwright")]
[Orleans.Metadata.DefaultGrainType("playwright")]
public interface IPlaywright : INeuron
{
    Task Attach(BrowserAttachment attachment, CancellationToken ct = default);
    Task Detach(string sessionId, CancellationToken ct = default);
    Task<BrowserObservation> Navigate(string url, CancellationToken ct = default);
    Task<BrowserObservation> Snapshot(CancellationToken ct = default);
    Task<BrowserObservation> Click(string selector, CancellationToken ct = default);
    Task<BrowserObservation> Fill(string selector, string value, CancellationToken ct = default);
    Task<BrowserSession> Read();
}
