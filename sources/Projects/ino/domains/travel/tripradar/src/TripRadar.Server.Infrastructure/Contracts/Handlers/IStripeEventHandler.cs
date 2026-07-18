using Stripe;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Infrastructure.Contracts.Handlers;

public interface IStripeEventHandler
{
    string EventType { get; }
    Task<Result> HandleAsync(Event stripeEvent, CancellationToken cancellationToken = default);
}
