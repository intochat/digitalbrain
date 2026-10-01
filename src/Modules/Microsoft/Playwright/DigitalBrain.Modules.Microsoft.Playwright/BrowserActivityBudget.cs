namespace DigitalBrain.Microsoft.Playwright;

// Each browser action owns a fresh budget; earlier in-flight responses retain their old budget.
internal sealed class BrowserActivityBudget
{
    private int _requests;
    private long _bytes;
    public bool TryRequest() => Interlocked.Increment(ref _requests) <= 2000;
    public bool TryReceive(int count) => Interlocked.Add(ref _bytes, count) <= 128 * 1024 * 1024;
}
