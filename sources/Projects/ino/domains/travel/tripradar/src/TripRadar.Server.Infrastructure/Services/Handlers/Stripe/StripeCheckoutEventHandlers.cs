using Microsoft.Extensions.Logging;
using Stripe.Checkout;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Handlers.Stripe;

public class CheckoutSessionCompletedHandler(IPaymentService paymentService, ILogger<CheckoutSessionCompletedHandler> logger) : SubscriptionProcessingStripeEventHandler<Session>(StripeConstants.WebhookEvents.CheckoutSession.Completed, session => session.SubscriptionId, paymentService, logger);
