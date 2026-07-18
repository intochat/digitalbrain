using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Comms.Core.SharedKernel;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class NoOpStripeEventHandler<TStripeEntity>(string eventType, ILogger logger)
    : StripeEventHandler<TStripeEntity>(logger)
    where TStripeEntity : class, IStripeEntity
{
    public override string EventType => eventType;

    protected override Task<Result> ProcessEventDataAsync(TStripeEntity eventData, Event stripeEvent, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
}
