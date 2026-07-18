namespace TripRadar.Server.Infrastructure.Contracts.Handlers;

public interface IStripeEventHandlerFactory
{
    IStripeEventHandler? GetHandler(string eventType);
}
