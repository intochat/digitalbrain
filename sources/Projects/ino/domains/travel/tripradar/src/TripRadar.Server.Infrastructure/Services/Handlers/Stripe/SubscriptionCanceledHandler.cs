using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class SubscriptionCanceledHandler(IPaymentService paymentService, ILogger<SubscriptionCanceledHandler> logger) : SubscriptionProcessingStripeEventHandler<Subscription>(StripeConstants.WebhookEvents.Subscription.Canceled, subscription => subscription.Id, paymentService, logger);
