using Microsoft.Extensions.Logging;
using Stripe;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class SubscriptionDeletedHandler(IPaymentService paymentService, ILogger<SubscriptionDeletedHandler> logger) : SubscriptionProcessingStripeEventHandler<Subscription>(StripeConstants.WebhookEvents.Subscription.Deleted, subscription => subscription.Id, paymentService, logger);
