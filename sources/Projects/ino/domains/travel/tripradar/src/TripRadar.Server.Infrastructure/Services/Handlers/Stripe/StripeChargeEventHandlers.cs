using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class ChargeSucceededHandler(ILogger<ChargeSucceededHandler> logger) : NoOpStripeEventHandler<Charge>(StripeConstants.WebhookEvents.Charge.Succeeded, logger);
