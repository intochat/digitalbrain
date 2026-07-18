namespace TripRadar.Server.Infrastructure.Constants;

public static class EmailConstants
{
    public const string DefaultLanguage = "en";

    public const string ResourcesPath = "Resources";
    public const string LocalizationPath = "Localization";
    public const string JsonFileExtension = ".json";

    public static class Placeholders
    {
        public const string Title = "{{TITLE}}";
        public const string HeaderColor = "{{HEADER_COLOR}}";
        public const string MainHeading = "{{MAIN_HEADING}}";
        public const string Username = "{{USERNAME}}";
        public const string Message = "{{MESSAGE}}";
        public const string ButtonText = "{{BUTTON_TEXT}}";
        public const string ButtonColor = "{{BUTTON_COLOR}}";
        public const string ButtonHoverColor = "{{BUTTON_HOVER_COLOR}}";
        public const string ActionUrl = "{{ACTION_URL}}";
        public const string SecondaryMessage = "{{SECONDARY_MESSAGE}}";
        public const string Features = "{{FEATURES}}";
        public const string ExpiryInfo = "{{EXPIRY_INFO}}";
        public const string IgnoreText = "{{IGNORE_TEXT}}";
        public const string Greeting = "{{GREETING}}";
        public const string UrlFallbackLabel = "{{URL_FALLBACK_LABEL}}";
        public const string FooterBestRegards = "{{FOOTER_BEST_REGARDS}}";
        public const string FooterTeamName = "{{FOOTER_TEAM_NAME}}";
        public const string FooterCopyright = "{{FOOTER_COPYRIGHT}}";
        public const string FooterTagline = "{{FOOTER_TAGLINE}}";
        public const string SocialMediaLinks = "{{SOCIAL_MEDIA_LINKS}}";
        public const string CompanyAddress = "{{COMPANY_ADDRESS}}";
        public const string UnsubscribeUrl = "{{UNSUBSCRIBE_URL}}";
        public const string UnsubscribeText = "{{UNSUBSCRIBE_TEXT}}";
        public const string LogoUrl = "{{LOGO_URL}}";
        public const string SignatureImageUrl = "{{SIGNATURE_IMAGE_URL}}";
    }

    public static class Sections
    {
        public const string EmailConfirmation = "EmailConfirmation";
        public const string PasswordReset = "PasswordReset";
        public const string SubscriptionCancellation = "SubscriptionCancellation";
        public const string SubscriptionCreated = "SubscriptionCreated";
        public const string SubscriptionUpgraded = "SubscriptionUpgraded";
        public const string SubscriptionDowngraded = "SubscriptionDowngraded";
        public const string SubscriptionDowngradeScheduled = "SubscriptionDowngradeScheduled";
        public const string RefundProcessed = "RefundProcessed";
        public const string PaymentMethodUpdated = "PaymentMethodUpdated";
        public const string Common = "Common";
    }

    public static class Keys
    {
        public const string Title = "Title";
        public const string MainHeading = "MainHeading";
        public const string Message = "Message";
        public const string MessageWithReason = "MessageWithReason";
        public const string ButtonText = "ButtonText";
        public const string SecondaryMessage = "SecondaryMessage";
        public const string ExpiryInfo = "ExpiryInfo";
        public const string IgnoreText = "IgnoreText";
        public const string Features = "Features";
        public const string Greeting = "Greeting";
        public const string FooterBestRegards = "FooterBestRegards";
        public const string FooterTeamName = "FooterTeamName";
        public const string FooterCopyright = "FooterCopyright";
        public const string FooterTagline = "FooterTagline";
        public const string CompanyAddress = "CompanyAddress";
        public const string UnsubscribeText = "UnsubscribeText";
    }

    public static class CommonCategories
    {
        public const string HeaderColors = "HeaderColors";
        public const string ButtonColors = "ButtonColors";
        public const string ButtonHoverColors = "ButtonHoverColors";
    }

    public static class DataKeys
    {
        public const string CancellationReason = "cancellationReason";
        public const string TierName = "tierName";
        public const string Amount = "amount";
        public const string BillingPeriod = "billingPeriod";
        public const string NextBillingDate = "nextBillingDate";
        public const string OldTierName = "oldTierName";
        public const string NewTierName = "newTierName";
        public const string EffectiveDate = "effectiveDate";
        public const string RefundAmount = "refundAmount";
        public const string Currency = "currency";
        public const string ProcessedDate = "processedDate";
        public const string UpdatedDate = "updatedDate";
        public const string CurrentTierName = "currentTierName";
        public const string TargetTierName = "targetTierName";
        public const string Reason = "reason";
    }

    public static class ApiEndpoints
    {
        public const string ConfirmEmail = "/api/v1/users/email-confirmations";
        public const string ResetPassword = "/api/v1/users/password-resets";
    }

    public static class HtmlStyles
    {
        public const string FeaturesList = "color: #4b5563; font-size: 15px; line-height: 1.8; padding-left: 20px; margin: 15px 0;";
    }

    public static class DateFormats
    {
        public const string StandardDate = "MMMM dd, yyyy";
        public const string DateTimeWithTime = "MMMM dd, yyyy 'at' h:mm tt";
    }

    public static class CurrencyFormats
    {
        public const string UsdFormat = "${0:F2}";
        public const string GenericFormat = "{0} {1:F2}";
    }

    public static class Fallbacks
    {
        public const string NoActionUrl = "#";
        public const string MissingTranslation = "[{0}.{1}]";
    }

    public static class Subjects
    {
        public const string EmailConfirmation = "Confirm Your Email Address - TripRadar";
        public const string PasswordReset = "Password Reset Request - TripRadar";
        public const string SubscriptionCancellation = "Subscription Cancellation - TripRadar";
        public const string SubscriptionCreated = "Welcome to Your Subscription - TripRadar";
        public const string SubscriptionUpgraded = "Subscription Upgraded - TripRadar";
        public const string SubscriptionDowngraded = "Subscription Plan Changed - TripRadar";
        public const string SubscriptionDowngradeScheduled = "Subscription Downgrade Scheduled - TripRadar";
        public const string RefundProcessed = "Refund Processed - TripRadar";
    }
}
