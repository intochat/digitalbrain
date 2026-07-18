using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class PaymentIntentCreatedHandler(ILogger<PaymentIntentCreatedHandler> logger) : NoOpStripeEventHandler<PaymentIntent>(StripeConstants.WebhookEvents.PaymentIntent.Created, logger);
