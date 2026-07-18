using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class PaymentIntentSucceededHandler(ILogger<PaymentIntentSucceededHandler> logger)
    : NoOpStripeEventHandler<PaymentIntent>(StripeConstants.WebhookEvents.PaymentIntent.Succeeded, logger);
