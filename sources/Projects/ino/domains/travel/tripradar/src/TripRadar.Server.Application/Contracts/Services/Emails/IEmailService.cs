namespace TripRadar.Server.Application.Contracts.Services.Emails;

public interface IEmailService
{
    Task<bool> SendEmailConfirmationAsync(string toEmail, string confirmationToken, CancellationToken cancellationToken = default);

    Task<bool> SendPasswordResetAsync(string toEmail, string username, string resetToken, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendSubscriptionCancellationAsync(string toEmail, string username, string? cancellationReason = null, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendSubscriptionCreatedAsync(string toEmail, string username, string tierName, decimal amount, string billingPeriod, DateTime nextBillingDate, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendSubscriptionUpgradedAsync(string toEmail, string username, string oldTierName, string newTierName, decimal newAmount, string billingPeriod, DateTime nextBillingDate, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendSubscriptionDowngradedAsync(string toEmail, string username, string oldTierName, string newTierName, decimal newAmount, string billingPeriod, DateTime effectiveDate, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendRefundProcessedAsync(string toEmail, string username, decimal refundAmount, string currency, string reason, DateTime processedDate, string? languageCode = null, CancellationToken cancellationToken = default);

    Task<bool> SendSubscriptionDowngradeScheduledAsync(string toEmail, string username, string currentTierName, string targetTierName, DateTime effectiveDate, string? languageCode = null, CancellationToken cancellationToken = default);
}
