using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Comms.Core.SharedKernel;
using TripRadar.Server.Infrastructure.Contracts.Handlers;
using TripRadar.Server.Infrastructure.Extensions;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public abstract class StripeEventHandler<TStripeEntity>(ILogger logger) : IStripeEventHandler
    where TStripeEntity : class, IStripeEntity
{
    protected ILogger Logger { get; } = logger;

    public abstract string EventType { get; }

    public async Task<Result> HandleAsync(Event stripeEvent, CancellationToken cancellationToken = default)
    {
        var eventData = stripeEvent.ExtractEventData<TStripeEntity>();
        if (eventData is not null)
        {
            return await ProcessEventDataAsync(eventData, stripeEvent, cancellationToken);
        }

        Logger.LogError(
            "Invalid {DataType} data in event {EventType} with ID {EventId}",
            typeof(TStripeEntity).Name.ToLowerInvariant(),
            stripeEvent.Type,
            stripeEvent.Id);

        return await HandleInvalidDataAsync();
    }

    protected abstract Task<Result> ProcessEventDataAsync(
        TStripeEntity eventData,
        Event stripeEvent,
        CancellationToken cancellationToken);

    protected virtual Task<Result> HandleInvalidDataAsync() => Task.FromResult(Result.Success());
}
