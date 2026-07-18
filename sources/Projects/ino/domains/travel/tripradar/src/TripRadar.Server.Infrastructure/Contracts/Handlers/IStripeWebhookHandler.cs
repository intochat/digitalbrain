namespace TripRadar.Server.Infrastructure.Contracts.Handlers;

public interface IStripeWebhookHandler
{
    Task<bool> HandleWebhookAsync(string? payload, string? signature, CancellationToken cancellationToken = default);
}
