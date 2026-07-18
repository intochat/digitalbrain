using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Repositories;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Comms.Core.Helpers;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services.Emails;

public class EmailService(
    IOptions<EmailSettings> emailSettings,
    ILogger<EmailService> logger,
    IEmailTemplateGeneratorService templateGeneratorService,
    IUnitOfWork unitOfWork) : IEmailService
{
    private readonly EmailSettings _emailSettings = emailSettings.Value;
    private EmailClient? _emailClient;

    public async Task<bool> SendEmailConfirmationAsync(string toEmail, string confirmationToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var confirmationUrl = BuildFrontendAuthUrl("/confirm-email", ("email", toEmail), ("token", confirmationToken));
            if (confirmationUrl == null)
            {
                logger.LogError("Email confirmation cannot be sent because EmailSettings.RedirectUrl is missing or invalid.");
                return false;
            }

            var body = await templateGeneratorService.GenerateEmailAsync(
                EmailType.EmailConfirmation,
                new EmailParameters(
                    toEmail,
                    null,
                    confirmationUrl,
                    null,
                    EmailType.EmailConfirmation,
                    BuildUnsubscribeUrl("email", toEmail)),
                cancellationToken);
            return await SendEmailAsync(toEmail, EmailConstants.Subjects.EmailConfirmation, body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email confirmation to {Email}", StringHelper.MaskEmail(toEmail));
            return false;
        }
    }

    public async Task<bool> SendPasswordResetAsync(string toEmail, string username, string resetToken, string? languageCode = null, CancellationToken cancellationToken = default)
    {
        var resetUrl = BuildFrontendAuthUrl("/reset-password", ("username", username), ("token", resetToken));
        if (resetUrl == null)
        {
            logger.LogError("Email password reset cannot be sent because EmailSettings.RedirectUrl is missing or invalid.");
            return false;
        }

        return await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.PasswordReset,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.PasswordReset,
                new EmailParameters(
                    username,
                    lang,
                    resetUrl,
                    null,
                    EmailType.PasswordReset,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);
    }

    public async Task<bool> SendSubscriptionCancellationAsync(string toEmail, string username, string? cancellationReason = null, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.SubscriptionCancellation,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.SubscriptionCancellation,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    string.IsNullOrWhiteSpace(cancellationReason) ? null : new Dictionary<string, object>
                    {
                        ["cancellationReason"] = cancellationReason
                    },
                    EmailType.SubscriptionCancellation,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    public async Task<bool> SendSubscriptionCreatedAsync(string toEmail, string username, string tierName, decimal amount, string billingPeriod, DateTime nextBillingDate, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.SubscriptionCreated,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.SubscriptionCreated,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    new Dictionary<string, object>
                    {
                        [EmailConstants.DataKeys.TierName] = tierName,
                        [EmailConstants.DataKeys.Amount] = amount,
                        [EmailConstants.DataKeys.BillingPeriod] = billingPeriod,
                        [EmailConstants.DataKeys.NextBillingDate] = nextBillingDate
                    },
                    EmailType.SubscriptionCreated,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    public async Task<bool> SendSubscriptionUpgradedAsync(string toEmail, string username, string oldTierName, string newTierName, decimal newAmount, string billingPeriod, DateTime nextBillingDate, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.SubscriptionUpgraded,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.SubscriptionUpgraded,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    new Dictionary<string, object>
                    {
                        [EmailConstants.DataKeys.OldTierName] = oldTierName,
                        [EmailConstants.DataKeys.NewTierName] = newTierName,
                        [EmailConstants.DataKeys.Amount] = newAmount,
                        [EmailConstants.DataKeys.BillingPeriod] = billingPeriod,
                        [EmailConstants.DataKeys.NextBillingDate] = nextBillingDate
                    },
                    EmailType.SubscriptionUpgraded,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    public async Task<bool> SendSubscriptionDowngradedAsync(string toEmail, string username, string oldTierName, string newTierName, decimal newAmount, string billingPeriod, DateTime effectiveDate, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.SubscriptionDowngraded,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.SubscriptionDowngraded,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    new Dictionary<string, object>
                    {
                        [EmailConstants.DataKeys.OldTierName] = oldTierName,
                        [EmailConstants.DataKeys.NewTierName] = newTierName,
                        [EmailConstants.DataKeys.Amount] = newAmount,
                        [EmailConstants.DataKeys.BillingPeriod] = billingPeriod,
                        [EmailConstants.DataKeys.EffectiveDate] = effectiveDate
                    },
                    EmailType.SubscriptionDowngraded,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    public async Task<bool> SendRefundProcessedAsync(string toEmail, string username, decimal refundAmount, string currency, string reason, DateTime processedDate, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.RefundProcessed,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.RefundProcessed,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    new Dictionary<string, object>
                    {
                        [EmailConstants.DataKeys.RefundAmount] = refundAmount,
                        [EmailConstants.DataKeys.Currency] = currency,
                        [EmailConstants.DataKeys.Reason] = reason,
                        [EmailConstants.DataKeys.ProcessedDate] = processedDate
                    },
                    EmailType.RefundProcessed,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    public async Task<bool> SendSubscriptionDowngradeScheduledAsync(string toEmail, string username, string currentTierName, string targetTierName, DateTime effectiveDate, string? languageCode = null, CancellationToken cancellationToken = default) =>
        await SendEmailWithTemplateAsync(toEmail, username, EmailConstants.Subjects.SubscriptionDowngradeScheduled,
            async lang => await templateGeneratorService.GenerateEmailAsync(
                EmailType.SubscriptionDowngradeScheduled,
                new EmailParameters(
                    username,
                    lang,
                    null,
                    new Dictionary<string, object>
                    {
                        [EmailConstants.DataKeys.CurrentTierName] = currentTierName,
                        [EmailConstants.DataKeys.TargetTierName] = targetTierName,
                        [EmailConstants.DataKeys.EffectiveDate] = effectiveDate
                    },
                    EmailType.SubscriptionDowngradeScheduled,
                    BuildUnsubscribeUrl("username", username)),
                cancellationToken),
            languageCode,
            cancellationToken);

    private async Task<bool> SendEmailWithTemplateAsync(
        string toEmail,
        string username,
        string subject,
        Func<string?, Task<string>> templateGenerator,
        string? languageCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var lang = languageCode ?? await GetUserLanguageAsync(username, cancellationToken);
            var body = await templateGenerator(lang);
            var result = await SendEmailAsync(toEmail, subject, body, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email} for user {Username}", StringHelper.MaskEmail(toEmail), username);
            return false;
        }
    }

    private async Task<string?> GetUserLanguageAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            var user = await unitOfWork.UserRepository.GetByUsernameReadOnlyAsync(username, cancellationToken);
            return user?.Profile.Language?.LanguageCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get language for user {Username}, using default", username);
            return null;
        }
    }

    private async Task<bool> SendEmailAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            var client = GetOrCreateEmailClient();
            if (client is null)
            {
                return false;
            }

            var emailMessage = new EmailMessage(
                senderAddress: _emailSettings.SenderEmail,
                content: new EmailContent(subject)
                {
                    Html = body
                },
                recipients: new EmailRecipients(new List<EmailAddress> { new(toEmail) }));

            var sendOperation = await client.SendAsync(WaitUntil.Completed, emailMessage, cancellationToken);
            if (!string.Equals(sendOperation.Value.Status.ToString(), "Succeeded", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogError(
                    "Email sending completed with non-success status {Status} for recipient {Email}.",
                    sendOperation.Value.Status,
                    StringHelper.MaskEmail(toEmail));
                return false;
            }

            return true;
        }
        catch (RequestFailedException rfEx)
        {
            logger.LogError(
                rfEx,
                "Azure Communication Services error while sending email from {SenderEmail} to {Email}. Status: {Status}, ErrorCode: {ErrorCode}, Message: {Message}",
                _emailSettings.SenderEmail,
                StringHelper.MaskEmail(toEmail),
                rfEx.Status,
                rfEx.ErrorCode,
                rfEx.Message);
            return false;
        }
        catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
        {
            logger.LogError(tcEx, "Timeout occurred while sending email to {Email} via Azure Communication Services", StringHelper.MaskEmail(toEmail));
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error occurred while sending email to {Email} via Azure Communication Services. Exception type: {ExceptionType}", StringHelper.MaskEmail(toEmail), ex.GetType().Name);
            return false;
        }
    }

    private string? BuildFrontendAuthUrl(string routePath, params (string Key, string Value)[] parameters)
    {
        if (!TryGetAbsoluteHttpUrl(_emailSettings.RedirectUrl, out var baseUri))
        {
            return null;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var fragment = string.Join("&", parameters.Select(parameter => $"{parameter.Key}={Uri.EscapeDataString(parameter.Value)}"));

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{basePath}{routePath}",
            Fragment = fragment
        };

        return builder.Uri.ToString();
    }

    private string? BuildUnsubscribeUrl(string parameterName, string parameterValue)
    {
        if (!TryGetAbsoluteHttpUrl(_emailSettings.RedirectUrl, out var baseUri))
        {
            logger.LogWarning("EmailSettings.RedirectUrl is missing or invalid. Unsubscribe link will be omitted.");
            return null;
        }

        var basePath = baseUri.AbsolutePath.TrimEnd('/');
        var builder = new UriBuilder(baseUri)
        {
            Path = $"{basePath}/unsubscribe",
            Query = $"{parameterName}={Uri.EscapeDataString(parameterValue)}"
        };

        return builder.Uri.ToString();
    }

    private static bool TryGetAbsoluteHttpUrl(string? url, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private EmailClient? GetOrCreateEmailClient()
    {
        if (_emailClient is not null)
        {
            return _emailClient;
        }

        if (string.IsNullOrWhiteSpace(_emailSettings.ConnectionString))
        {
            logger.LogError("EmailSettings.ConnectionString is missing. Email sending is disabled.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(_emailSettings.SenderEmail))
        {
            logger.LogError("EmailSettings.SenderEmail is missing. Email sending is disabled.");
            return null;
        }

        try
        {
            _emailClient = new EmailClient(_emailSettings.ConnectionString);
            logger.LogInformation("Azure EmailClient initialized with sender {SenderEmail}", _emailSettings.SenderEmail);
            return _emailClient;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize Azure EmailClient. Check EmailSettings.ConnectionString.");
            return null;
        }
    }
}
