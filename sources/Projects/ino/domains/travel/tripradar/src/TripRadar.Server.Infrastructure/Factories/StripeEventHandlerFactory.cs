using TripRadar.Server.Infrastructure.Contracts.Handlers;

namespace TripRadar.Server.Infrastructure.Factories;

public class StripeEventHandlerFactory(IEnumerable<IStripeEventHandler> handlers) : IStripeEventHandlerFactory
{
    private readonly Dictionary<string, IStripeEventHandler> _handlers = CreateHandlerDictionary(handlers);

    public IStripeEventHandler? GetHandler(string eventType) => _handlers.GetValueOrDefault(eventType);

    private static Dictionary<string, IStripeEventHandler> CreateHandlerDictionary(
        IEnumerable<IStripeEventHandler> handlers) =>
        handlers.ToDictionary(h => h.EventType, h => h);
}
