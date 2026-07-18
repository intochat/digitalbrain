using Microsoft.Extensions.DependencyInjection;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Payments;
using TripRadar.Server.Infrastructure.Contracts;
using TripRadar.Server.Infrastructure.Contracts.Handlers;
using TripRadar.Server.Infrastructure.Factories;
using TripRadar.Server.Infrastructure.Providers.Stripe.Client;
using TripRadar.Server.Infrastructure.Services;
using TripRadar.Server.Infrastructure.Services.Emails;
using TripRadar.Server.Infrastructure.Services.Handlers;
using TripRadar.Server.Infrastructure.Services.Handlers.Stripe;
using TripRadar.Server.Infrastructure.Services.Payments;
using TripRadar.Server.Infrastructure.Services.Payments.Internal;
using TripRadar.Server.Infrastructure.Services.UserLimits;

namespace TripRadar.Server.Infrastructure.Extensions;

public static partial class ServiceCollectionExtensions
{
    private static void ConfigurePaymentServices(this IServiceCollection services) =>
        services
            .AddScoped<IStripeApiProvider, StripeApiProvider>()
            .AddScoped<IStripeGateway, StripeGateway>()
            .AddScoped<SubscriptionRecordService>()
            .AddScoped<DeferredDowngradeService>()
            .AddScoped<ISubscriptionCheckoutService, SubscriptionCheckoutService>()
            .AddScoped<ISubscriptionLifecycleService, SubscriptionLifecycleService>()
            .AddScoped<ISubscriptionStateService, SubscriptionStateService>()
            .AddScoped<ISubscriptionEmailService, SubscriptionEmailService>()
            .AddScoped<ISubscriptionWebhookHandler, SubscriptionWebhookHandler>()
            .AddScoped<ISubscriptionManagementService, SubscriptionManagementService>()
            .AddScoped<IPaymentService, PaymentService>()
            .AddScoped<IRefundService, RefundService>()
            .AddScoped<IPromoCodeValidationService, PromoCodeValidationService>()
            .AddScoped<IPromoCodeService, PromoCodeService>()
            .AddScoped<IOverageBillingService, OverageBillingService>()
            .AddScoped<ISubscriptionPolicy, SubscriptionPolicy>()
            .AddScoped<ITierLimitService, TierLimitService>()
            .AddScoped<UserLimitUserLookup>()
            .AddScoped<UserLimitDecisionService>()
            .AddScoped<UserTokenReservationService>()
            .AddScoped<UserTokenCommitService>()
            .AddScoped<IUserLimitService, UserLimitService>()
            .AddStripeWebhookServices();

    private static void AddStripeWebhookServices(this IServiceCollection services) =>
        services
            .AddScoped<IStripeWebhookHandler, StripeWebhookHandler>()
            .AddScoped<IStripeEventHandlerFactory, StripeEventHandlerFactory>()
            .AddScoped<IStripeEventHandler, ChargeSucceededHandler>()
            .AddScoped<IStripeEventHandler, SubscriptionCreatedHandler>()
            .AddScoped<IStripeEventHandler, SubscriptionUpdatedHandler>()
            .AddScoped<IStripeEventHandler, SubscriptionDeletedHandler>()
            .AddScoped<IStripeEventHandler, SubscriptionCanceledHandler>()
            .AddScoped<IStripeEventHandler, InvoicePaymentSucceededHandler>()
            .AddScoped<IStripeEventHandler, InvoiceItemCreatedHandler>()
            .AddScoped<IStripeEventHandler, PaymentIntentCreatedHandler>()
            .AddScoped<IStripeEventHandler, PaymentIntentSucceededHandler>()
            .AddScoped<IStripeEventHandler, CheckoutSessionCompletedHandler>();
}