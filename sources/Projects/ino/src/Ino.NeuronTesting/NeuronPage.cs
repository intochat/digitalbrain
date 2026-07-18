using Microsoft.Playwright;

namespace Ino.NeuronTesting;

public sealed class NeuronPage : IAsyncDisposable
{
    readonly IBrowserContext _context;

    internal NeuronPage(IBrowserContext context, IPage playwright)
    {
        _context = context;
        Playwright = playwright;
    }

    // Escape hatch for Playwright APIs the wrapper hasn't surfaced.
    public IPage Playwright { get; }

    public Task<byte[]> Screenshot() => Playwright.ScreenshotAsync(new() { FullPage = true });

    public async ValueTask DisposeAsync()
    {
        await Playwright.CloseAsync();
        await _context.CloseAsync();
    }
}
