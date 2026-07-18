namespace TripRadar.Server.Application.Contracts.Services.Emails;

public enum EmailType
{
    EmailConfirmation = 1,
    PasswordReset = 2,
    SubscriptionCancellation = 3,
    SubscriptionCreated = 4,
    SubscriptionUpgraded = 5,
    SubscriptionDowngraded = 6,
    RefundProcessed = 7,
    PaymentMethodUpdated = 8,
    SubscriptionDowngradeScheduled = 9
}
