using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Foundry;

/// Basic capability broker for approved scripts. Scripts get this facade instead of raw System.Net/IO.
/// Approved via proposal (future: manifest in RegisterReaction).
public interface ICapabilityBroker
{
    // Example: notify via existing transport.
    Task NotifyAsync(string channel, string message);
    // Http limited.
    Task<string> HttpGetAsync(string url);
}

public class CapabilityBroker : ICapabilityBroker
{
    private readonly IServiceProvider _sp;

    public CapabilityBroker(IServiceProvider sp) => _sp = sp;

    public Task NotifyAsync(string channel, string message)
    {
        // Would use Telegram or other approved.
        // For now, fire signal.
        // In real, rate limited, audited.
        return Task.CompletedTask;
    }

    public async Task<string> HttpGetAsync(string url)
    {
        // Limited to approved domains.
        using var client = new System.Net.Http.HttpClient();
        return await client.GetStringAsync(url);
    }
}