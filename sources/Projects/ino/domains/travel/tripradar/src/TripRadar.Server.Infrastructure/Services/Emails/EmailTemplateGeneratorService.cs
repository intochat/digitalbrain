using System.Net;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using TripRadar.Server.Application.Contracts.Services;
using TripRadar.Server.Application.Contracts.Services.Emails;
using TripRadar.Server.Infrastructure.Constants;
using TripRadar.Server.Infrastructure.Services.Emails.Builders;
using TripRadar.Server.Infrastructure.Settings;

namespace TripRadar.Server.Infrastructure.Services.Emails;

public class EmailTemplateGeneratorService(
    ITranslationService translationService,
    ILanguageResolver languageResolver,
    IOptions<EmailSettings> emailSettings)
    : IEmailTemplateGeneratorService
{
    private readonly ITranslationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    private readonly ILanguageResolver _languageResolver = languageResolver ?? throw new ArgumentNullException(nameof(languageResolver));
    private readonly EmailSettings _emailSettings = emailSettings.Value ?? throw new ArgumentNullException(nameof(emailSettings));
    private readonly EmailTemplateBuilder _templateBuilder = new();

    public Task<string> GenerateEmailAsync(EmailType emailType, EmailParameters parameters, CancellationToken cancellationToken = default)
    {
        return GenerateAsync(parameters with { EmailType = emailType }, cancellationToken);
    }

    private async Task<string> GenerateAsync(EmailParameters parameters, CancellationToken cancellationToken = default)
    {
        ValidateParameters(parameters);

        var language = await _languageResolver.ResolveLanguageAsync(parameters.LanguageCode, cancellationToken);
        var section = GetSectionName(parameters.EmailType);
        var safeUsername = WebUtility.HtmlEncode(parameters.UsernameOrEmail);
        var currentYear = DateTime.UtcNow.Year;
        var data = parameters.Data ?? new Dictionary<string, object>();
        var actionUrl = ResolveActionUrl(parameters.ActionUrl, section);

        var featuresTask = BuildFeaturesAsync(language, section, data);
        var messageTask = GetCustomMessageAsync(language, section, data);
        var mainHeadingTask = BuildMainHeadingAsync(language, section, safeUsername, data);
        var expiryInfoTask = BuildExpiryInfoAsync(language, section, data);
        var titleTask = _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Title);
        var headerColorTask = _translationService.GetCommonTranslationAsync(language, EmailConstants.CommonCategories.HeaderColors, section);
        var buttonTextTask = _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.ButtonText);
        var buttonColorTask = _translationService.GetCommonTranslationAsync(language, EmailConstants.CommonCategories.ButtonColors, section);
        var buttonHoverColorTask = _translationService.GetCommonTranslationAsync(language, EmailConstants.CommonCategories.ButtonHoverColors, section);
        var secondaryMessageTask = _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.SecondaryMessage);
        var ignoreTextTask = _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.IgnoreText);
        var greetingTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.Greeting);
        var bestRegardsTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.FooterBestRegards);
        var footerTeamNameTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.FooterTeamName);
        var footerCopyrightTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.FooterCopyright, currentYear);
        var footerTaglineTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.FooterTagline);
        var companyAddressTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.CompanyAddress);
        var unsubscribeTextTask = _translationService.GetTranslationAsync(language, EmailConstants.Sections.Common, EmailConstants.Keys.UnsubscribeText);

        await Task.WhenAll(
            featuresTask,
            messageTask,
            mainHeadingTask,
            expiryInfoTask,
            titleTask,
            headerColorTask,
            buttonTextTask,
            buttonColorTask,
            buttonHoverColorTask,
            secondaryMessageTask,
            ignoreTextTask,
            greetingTask,
            bestRegardsTask,
            footerTeamNameTask,
            footerCopyrightTask,
            footerTaglineTask,
            companyAddressTask,
            unsubscribeTextTask);

        var title = await titleTask;
        var headerColor = NormalizeCssColor(await headerColorTask, "#3b82f6");
        var mainHeading = await mainHeadingTask;
        var message = await messageTask;
        var buttonText = await buttonTextTask;
        var buttonColor = NormalizeCssColor(await buttonColorTask, "#3b82f6");
        var buttonHoverColor = NormalizeCssColor(await buttonHoverColorTask, "#2563eb");
        var secondaryMessage = await secondaryMessageTask;
        var features = await featuresTask;
        var expiryInfo = await expiryInfoTask;
        var ignoreText = await ignoreTextTask;
        var greeting = await greetingTask;
        var footerBestRegards = await bestRegardsTask;
        var footerTeamName = await footerTeamNameTask;
        var footerCopyright = await footerCopyrightTask;
        var footerTagline = await footerTaglineTask;
        var companyAddress = await companyAddressTask;
        var unsubscribeText = await unsubscribeTextTask;

        return _templateBuilder
            .CreateEmailTemplate(GetEmailTemplate())
            .WithTitle(title)
            .WithHeaderColor(headerColor)
            .WithMainHeading(mainHeading)
            .WithUsername(safeUsername)
            .WithMessage(message)
            .WithButtonText(buttonText)
            .WithButtonColor(buttonColor)
            .WithButtonHoverColor(buttonHoverColor)
            .WithActionUrl(actionUrl)
            .WithSecondaryMessage(secondaryMessage)
            .WithFeatures(features)
            .WithExpiryInfo(expiryInfo)
            .WithIgnoreText(ignoreText)
            .WithGreeting(greeting)
            .WithFooterBestRegards(footerBestRegards)
            .WithFooterTeamName(footerTeamName)
            .WithFooterCopyright(footerCopyright)
            .WithFooterTagline(footerTagline)
            .WithSocialMediaLinks(BuildSocialMediaLinks())
            .WithCompanyAddress(companyAddress)
            .WithUnsubscribeUrl(parameters.UnsubscribeUrl ?? string.Empty)
            .WithUnsubscribeText(unsubscribeText)
            .WithLogoUrl(BuildLogo())
            .Build();
    }

    private string BuildSocialMediaLinks()
    {
        var socialLinks = new Dictionary<string, (string SocialUrl, string IconFileName)>
        {
            { "Telegram", (_emailSettings.SocialLinks.Telegram, "telegram.png") },
            { "Instagram", (_emailSettings.SocialLinks.Instagram, "instagram.png") },
            { "TikTok", (_emailSettings.SocialLinks.TikTok, "tiktok.png") },
            { "Facebook", (_emailSettings.SocialLinks.Facebook, "facebook.png") },
            { "X", (_emailSettings.SocialLinks.X, "twitter.png") },
            { "YouTube", (_emailSettings.SocialLinks.YouTube, "youtube.png") },
            { "LinkedIn", (_emailSettings.SocialLinks.LinkedIn, "linkedin.png") }
        };

        var html = new StringBuilder();

        foreach (var (key, value) in socialLinks)
        {
            var iconUrl = BuildAssetUrl(value.IconFileName);
            if (string.IsNullOrWhiteSpace(iconUrl) || string.IsNullOrWhiteSpace(value.SocialUrl))
            {
                continue;
            }

            html.Append(CultureInfo.InvariantCulture, $"""
                              <a href="{value.SocialUrl}" target="_blank" style="display:inline-block;margin:0 4px;text-decoration:none;">
                                  <img src="{iconUrl}" alt="{key}" width="24" height="24" style="border:0;display:inline-block;vertical-align:middle;pointer-events:none;user-select:none;-webkit-user-drag:none;-webkit-touch-callout:none;" draggable="false" oncontextmenu="return false;" />
                              </a>
                          """);
        }

        return html.ToString();
    }


    private string BuildLogo()
    {
        var logoUrl = ResolveLogoUrl();
        return string.IsNullOrWhiteSpace(logoUrl)
            ? string.Empty
            : $"<img src=\"{logoUrl}\" alt=\"TripRadar\" class=\"header-logo\" style=\"display:block;margin:0 auto;width:100%;max-width:300px;height:auto;pointer-events:none;user-select:none;-webkit-user-drag:none;-webkit-touch-callout:none;\" draggable=\"false\" oncontextmenu=\"return false;\" />";
    }

    private string? ResolveLogoUrl()
    {
        var configuredLogoUrl = ResolvePossiblyPlaceholderValue(_emailSettings.EmailLogoUrl);
        if (string.IsNullOrWhiteSpace(configuredLogoUrl)) configuredLogoUrl = ResolvePossiblyPlaceholderValue(_emailSettings.LogoUrl);

        if (!string.IsNullOrWhiteSpace(configuredLogoUrl))
            return TryGetAbsoluteHttpUrl(configuredLogoUrl, out var absoluteLogoUrl) ? absoluteLogoUrl.AbsoluteUri : BuildAssetUrl(configuredLogoUrl.Trim().TrimStart('/'));

        return BuildAssetUrl("tripradar-logo-brand.png") ?? BuildAssetUrl("logo-v2.png") ?? BuildAssetUrl("logo.png") ?? BuildAssetUrl("logo-v2.svg");
    }

    private static string NormalizeCssColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        var normalizedColor = color.Trim();
        if (normalizedColor.Contains('[') ||
            normalizedColor.Contains(']') ||
            normalizedColor.Contains('{') ||
            normalizedColor.Contains('}'))
        {
            return fallback;
        }

        if (normalizedColor.StartsWith('#') ||
            normalizedColor.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) ||
            normalizedColor.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase) ||
            normalizedColor.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase) ||
            normalizedColor.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedColor;
        }

        return fallback;
    }

    private string? BuildAssetUrl(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalizedBlobStorageUrl = ResolvePossiblyPlaceholderValue(_emailSettings.BlobStorageUrl);
        if (!TryGetAbsoluteHttpUrl(normalizedBlobStorageUrl, out var baseUri))
        {
            return null;
        }

        var filePath = fileName.Trim().TrimStart('/');
        var normalizedPath = baseUri.AbsolutePath.TrimEnd('/');

        var uriBuilder = new UriBuilder(baseUri)
        {
            Path = $"{normalizedPath}/{filePath}"
        };

        var normalizedSasToken = NormalizeSasToken(ResolvePossiblyPlaceholderValue(_emailSettings.BlobStorageSasToken));
        if (!string.IsNullOrWhiteSpace(normalizedSasToken))
        {
            uriBuilder.Query = normalizedSasToken;
        }

        return uriBuilder.Uri.AbsoluteUri;
    }

    private static string ResolvePossiblyPlaceholderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        if (trimmed.StartsWith("${", StringComparison.Ordinal) && trimmed.EndsWith('}'))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return string.Empty;
        }

        return trimmed;
    }

    private static string NormalizeSasToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var trimmed = token.Trim();
        return trimmed.StartsWith('?') ? trimmed[1..] : trimmed;
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

    private string ResolveActionUrl(string? actionUrl, string section)
    {
        if (TryGetAbsoluteHttpUrl(actionUrl, out var providedActionUri))
        {
            return providedActionUri.AbsoluteUri;
        }

        var defaultPath = section == EmailConstants.Sections.SubscriptionCancellation
            ? "/feedback"
            : "/profile/billing";

        var fallbackUrl = BuildAppUrlFromRedirect(defaultPath);
        return string.IsNullOrWhiteSpace(fallbackUrl) ? EmailConstants.Fallbacks.NoActionUrl : fallbackUrl;
    }

    private string? BuildAppUrlFromRedirect(string relativePath)
    {
        if (!TryGetAbsoluteHttpUrl(_emailSettings.RedirectUrl, out var baseUri))
        {
            return null;
        }

        var normalizedRelativePath = relativePath.StartsWith('/') ? relativePath : $"/{relativePath}";
        var basePath = baseUri.AbsolutePath.TrimEnd('/');

        var builder = new UriBuilder(baseUri)
        {
            Path = $"{basePath}{normalizedRelativePath}",
            Query = string.Empty
        };

        return builder.Uri.AbsoluteUri;
    }

    private async Task<string> BuildMainHeadingAsync(string language, string section, string username, Dictionary<string, object> data)
    {
        switch (section)
        {
            case EmailConstants.Sections.EmailConfirmation:
            case EmailConstants.Sections.SubscriptionCancellation:
                return await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.MainHeading, username);

            case EmailConstants.Sections.SubscriptionCreated:
            {
                var tier = data.TryGetValue(EmailConstants.DataKeys.TierName, out var t) ? t.ToString() : null;
                return await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.MainHeading, tier ?? string.Empty, username);
            }

            case EmailConstants.Sections.SubscriptionUpgraded:
            {
                var newTier = data.TryGetValue(EmailConstants.DataKeys.NewTierName, out var nt) ? nt.ToString() : null;
                return await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.MainHeading,
                    newTier ?? string.Empty);
            }

            default:
                return await _translationService.GetTranslationAsync(language, section,
                    EmailConstants.Keys.MainHeading);
        }
    }

    private static string GetSectionName(EmailType emailType) => emailType switch
    {
        EmailType.EmailConfirmation => EmailConstants.Sections.EmailConfirmation,
        EmailType.PasswordReset => EmailConstants.Sections.PasswordReset,
        EmailType.SubscriptionCancellation => EmailConstants.Sections.SubscriptionCancellation,
        EmailType.SubscriptionCreated => EmailConstants.Sections.SubscriptionCreated,
        EmailType.SubscriptionUpgraded => EmailConstants.Sections.SubscriptionUpgraded,
        EmailType.SubscriptionDowngraded => EmailConstants.Sections.SubscriptionDowngraded,
        EmailType.RefundProcessed => EmailConstants.Sections.RefundProcessed,
        EmailType.PaymentMethodUpdated => EmailConstants.Sections.PaymentMethodUpdated,
        EmailType.SubscriptionDowngradeScheduled => EmailConstants.Sections.SubscriptionDowngradeScheduled,
        _ => throw new ArgumentException($"Unsupported email type: {emailType}")
    };

    private async Task<string> GetCustomMessageAsync(string language, string section, Dictionary<string, object> data)
    {
        return section switch
        {
            EmailConstants.Sections.SubscriptionCancellation when data.ContainsKey(EmailConstants.DataKeys
                    .CancellationReason) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.MessageWithReason,
                    data[EmailConstants.DataKeys.CancellationReason]),

            EmailConstants.Sections.SubscriptionCreated when data.ContainsKey(EmailConstants.DataKeys.TierName) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message,
                    data[EmailConstants.DataKeys.TierName]),

            EmailConstants.Sections.SubscriptionUpgraded when data.ContainsKey(EmailConstants.DataKeys.OldTierName) &&
                                                              data.ContainsKey(EmailConstants.DataKeys.NewTierName) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message,
                    data[EmailConstants.DataKeys.OldTierName], data[EmailConstants.DataKeys.NewTierName]),

            EmailConstants.Sections.SubscriptionDowngraded when data.ContainsKey(EmailConstants.DataKeys.OldTierName) &&
                                                                data.ContainsKey(EmailConstants.DataKeys.NewTierName) &&
                                                                data.ContainsKey(EmailConstants.DataKeys
                                                                    .EffectiveDate) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message,
                    data[EmailConstants.DataKeys.OldTierName], data[EmailConstants.DataKeys.NewTierName],
                    FormatDate((DateTime)data[EmailConstants.DataKeys.EffectiveDate], language)),

            EmailConstants.Sections.RefundProcessed when data.ContainsKey(EmailConstants.DataKeys.RefundAmount) &&
                                                         data.ContainsKey(EmailConstants.DataKeys.Currency) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message,
                    FormatCurrency((decimal)data[EmailConstants.DataKeys.RefundAmount],
                        data[EmailConstants.DataKeys.Currency].ToString())),

            EmailConstants.Sections.SubscriptionDowngradeScheduled when
                data.ContainsKey(EmailConstants.DataKeys.CurrentTierName) &&
                data.ContainsKey(EmailConstants.DataKeys.TargetTierName) &&
                data.ContainsKey(EmailConstants.DataKeys.EffectiveDate) =>
                await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message,
                    data[EmailConstants.DataKeys.CurrentTierName], data[EmailConstants.DataKeys.TargetTierName],
                    FormatDate((DateTime)data[EmailConstants.DataKeys.EffectiveDate], language)),

            _ => await _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.Message)
        };
    }

    private Task<string> BuildExpiryInfoAsync(string language, string section, Dictionary<string, object> data)
    {
        if ((section == EmailConstants.Sections.SubscriptionDowngraded || section == EmailConstants.Sections.SubscriptionDowngradeScheduled) &&
            data.TryGetValue(EmailConstants.DataKeys.EffectiveDate, out var effectiveDateValue))
        {
            var formattedDate = TryFormatDateValue(effectiveDateValue, language);
            if (!string.IsNullOrWhiteSpace(formattedDate))
            {
                return _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.ExpiryInfo, formattedDate);
            }
        }

        return _translationService.GetTranslationAsync(language, section, EmailConstants.Keys.ExpiryInfo);
    }

    private static string? TryFormatDateValue(object dateValue, string language) =>
        dateValue switch
        {
            DateTime dateTime => FormatDate(dateTime, language),
            DateTimeOffset dateTimeOffset => FormatDate(dateTimeOffset.DateTime, language),
            DateOnly dateOnly => FormatDate(dateOnly.ToDateTime(TimeOnly.MinValue), language),
            string dateString when DateTime.TryParse(
                dateString,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsedInvariantDate) => FormatDate(parsedInvariantDate, language),
            string dateString when DateTime.TryParse(
                dateString,
                ResolveDateCulture(language),
                DateTimeStyles.AssumeLocal,
                out var parsedLocalizedDate) => FormatDate(parsedLocalizedDate, language),
            _ => null
        };

    private async Task<string> BuildFeaturesAsync(string language, string section, Dictionary<string, object> data)
    {
        var features = await _translationService.GetFeatureListAsync(language, section);

        if (data.Count > 0)
        {
            var customFeatures = await BuildCustomFeaturesAsync(language, section, data);
            return customFeatures.Any() ? BuildHtmlList(customFeatures) : BuildHtmlList(features);
        }

        return BuildHtmlList(features);
    }

    private async Task<List<string>> BuildCustomFeaturesAsync(string language, string section, Dictionary<string, object> data)
    {
        var features = new List<string>();

        switch (section)
        {
            case EmailConstants.Sections.SubscriptionCreated:
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesPlan", data, EmailConstants.DataKeys.TierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesAmount", data, EmailConstants.DataKeys.Amount, v => FormatCurrency((decimal)v));
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesBilling", data, EmailConstants.DataKeys.BillingPeriod);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesNextBilling", data, EmailConstants.DataKeys.NextBillingDate, v => FormatDate((DateTime)v, language));
                break;

            case EmailConstants.Sections.SubscriptionUpgraded:
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesUpgradedFrom", data, EmailConstants.DataKeys.OldTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesPlan", data, EmailConstants.DataKeys.NewTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesAmount", data, EmailConstants.DataKeys.Amount, v => FormatCurrency((decimal)v));
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesBilling", data, EmailConstants.DataKeys.BillingPeriod);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesNextBilling", data, EmailConstants.DataKeys.NextBillingDate, v => FormatDate((DateTime)v, language));
                break;

            case EmailConstants.Sections.SubscriptionDowngraded:
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesChangedFrom", data, EmailConstants.DataKeys.OldTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesPlan", data, EmailConstants.DataKeys.NewTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesAmount", data, EmailConstants.DataKeys.Amount, v => FormatCurrency((decimal)v));
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesBilling", data, EmailConstants.DataKeys.BillingPeriod);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesEffectiveDate", data, EmailConstants.DataKeys.EffectiveDate, v => FormatDate((DateTime)v, language));
                break;

            case EmailConstants.Sections.RefundProcessed:
                var reasonRaw = data.GetValueOrDefault(EmailConstants.DataKeys.Reason)?.ToString() ?? string.Empty;
                var translatedReason = await _translationService.GetTranslationAsync(language, section, reasonRaw);
                var formattedReason = !string.IsNullOrWhiteSpace(translatedReason) && translatedReason != reasonRaw ? translatedReason : reasonRaw;
                data[EmailConstants.DataKeys.Reason] = formattedReason;

                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesAmount", data, EmailConstants.DataKeys.RefundAmount, v => FormatCurrency((decimal)v, data.GetValueOrDefault(EmailConstants.DataKeys.Currency)?.ToString()));
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesProcessedDate", data, EmailConstants.DataKeys.ProcessedDate, v => FormatDate((DateTime)v, language));
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesReason", data, EmailConstants.DataKeys.Reason);
                break;

            case EmailConstants.Sections.PaymentMethodUpdated:
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "FeaturesUpdatedDate", data, EmailConstants.DataKeys.UpdatedDate, v => FormatDate((DateTime)v, language, "datetime"));
                break;

            case EmailConstants.Sections.SubscriptionDowngradeScheduled:
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "current_tier", data, EmailConstants.DataKeys.CurrentTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "target_tier", data, EmailConstants.DataKeys.TargetTierName);
                await AddTranslatedFeatureIfExistsAsync(features, language, section, "effective_date", data, EmailConstants.DataKeys.EffectiveDate, v => FormatDate((DateTime)v, language));
                break;
        }

        return features;
    }

    private async Task AddTranslatedFeatureIfExistsAsync(
        List<string> features,
        string language,
        string section,
        string translationKey,
        Dictionary<string, object> data,
        string dataKey,
        Func<object, string>? formatter = null)
    {
        if (data.TryGetValue(dataKey, out var value))
        {
            var formattedValue = formatter?.Invoke(value) ?? value.ToString() ?? string.Empty;
            var feature = await _translationService.GetTranslationAsync(language, section, translationKey, formattedValue);
            if (!string.IsNullOrWhiteSpace(feature))
            {
                features.Add(feature);
            }
        }
    }

    private static decimal ConvertFromCents(decimal amountInCents) => amountInCents / 100m;

    private static string FormatCurrency(decimal amountInCents, string? currency = null)
    {
        var amount = ConvertFromCents(amountInCents);
        return currency switch
        {
            null or "USD" => string.Format(CultureInfo.InvariantCulture, EmailConstants.CurrencyFormats.UsdFormat, amount),
            _ => string.Format(CultureInfo.InvariantCulture, EmailConstants.CurrencyFormats.GenericFormat, currency, amount)
        };
    }

    private static string FormatDate(DateTime date, string language, string? format = null)
    {
        var culture = ResolveDateCulture(language);
        var isEnglish = culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase);

        return format switch
        {
            "datetime" when isEnglish => date.ToString(EmailConstants.DateFormats.DateTimeWithTime, culture),
            "datetime" => date.ToString("dd MMMM yyyy 'в' HH:mm", culture),
            _ when isEnglish => date.ToString(EmailConstants.DateFormats.StandardDate, culture),
            _ => date.ToString("dd MMMM yyyy", culture)
        };
    }

    private static CultureInfo ResolveDateCulture(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private static string BuildHtmlList(IEnumerable<string> items)
    {
        var builder = new StringBuilder($"<ul style='{EmailConstants.HtmlStyles.FeaturesList}'>");
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            builder.Append("<li>")
                .Append(WebUtility.HtmlEncode(item))
                .Append("</li>");
        }

        builder.Append("</ul>");
        return builder.ToString();
    }

    private static void ValidateParameters(EmailParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (string.IsNullOrWhiteSpace(parameters.UsernameOrEmail))
        {
            throw new ArgumentException("Username cannot be null or empty", nameof(parameters));
        }

        ValidateUrl(parameters.ActionUrl, nameof(parameters.ActionUrl));
        ValidateUrl(parameters.UnsubscribeUrl, nameof(parameters.UnsubscribeUrl));
    }

    private static void ValidateUrl(string? url, string paramName)
    {
        if (string.IsNullOrEmpty(url)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult) ||
            (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"Invalid URL provided for {paramName}. Must be a valid absolute HTTP/HTTPS URL.", paramName);
        }
    }

    private static string GetEmailTemplate() =>
        """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <meta http-equiv="X-UA-Compatible" content="IE=edge">
            <title>{{TITLE}}</title>
            <style>
                body {
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                    margin: 0;
                    padding: 0;
                    background-color: #eef2f7;
                    color: #1f2937;
                    line-height: 1.6;
                }
                .email-wrapper {
                    padding: 24px 10px;
                }
                .email-container {
                    max-width: 640px;
                    margin: 0 auto;
                    background-color: #ffffff;
                    border-radius: 14px;
                    overflow: hidden;
                    box-shadow: 0 14px 32px rgba(15, 23, 42, 0.12);
                }
                .email-header {
                    padding: 22px 24px 8px;
                    text-align: center;
                    background: linear-gradient(180deg, #f3f8ff 0%, #ffffff 100%);
                }
                .header-logo {
                    height: auto;
                    margin-bottom: 4px;
                    -webkit-user-drag: none;
                    -webkit-touch-callout: none;
                }
                .email-content {
                    padding: 24px;
                }
                .main-heading {
                    font-size: 27px;
                    font-weight: 700;
                    color: #0f172a;
                    margin: 0 0 12px;
                    text-align: center;
                }
                .greeting {
                    font-size: 16px;
                    color: #334155;
                    margin: 0 0 10px;
                }
                .message {
                    font-size: 16px;
                    color: #374151;
                    margin: 0;
                }
                .cta-container {
                    text-align: center;
                    margin: 18px 0 14px;
                }
                .cta-button {
                    display: inline-block;
                    background-color: {{BUTTON_COLOR}};
                    background: linear-gradient(135deg, {{BUTTON_COLOR}} 0%, {{BUTTON_HOVER_COLOR}} 100%);
                    color: #ffffff !important;
                    text-decoration: none;
                    font-weight: 700;
                    font-size: 16px;
                    padding: 14px 26px;
                    border-radius: 10px;
                    box-shadow: 0 8px 16px rgba(15, 23, 42, 0.18);
                }
                .features-section {
                    background-color: #f8fafc;
                    border: 1px solid #e2e8f0;
                    border-radius: 12px;
                    padding: 14px;
                    margin: 0 0 14px;
                }
                .features-title {
                    font-size: 16px;
                    font-weight: 700;
                    color: #1f2937;
                    margin: 0 0 6px;
                }
                .highlight-note {
                    margin: 0 0 10px;
                    padding: 10px 12px;
                    border-radius: 10px;
                    background-color: #eff6ff;
                    color: #1e40af;
                    font-size: 14px;
                }
                .ignore-note {
                    margin: 0 0 18px;
                    padding: 10px 12px;
                    border-radius: 10px;
                    background-color: #fef2f2;
                    color: #991b1b;
                    font-size: 13px;
                }
                .footer {
                    border-top: 1px solid #e2e8f0;
                    padding-top: 16px;
                }
                .footer-text {
                    color: #64748b;
                    font-size: 14px;
                    margin: 0;
                    line-height: 1.6;
                    text-align: center;
                }
                .footer-team {
                    color: {{HEADER_COLOR}};
                    font-weight: 700;
                    margin-top: 6px;
                    text-align: center;
                }
                .social-section {
                    padding: 14px 24px 20px;
                    text-align: center;
                    background-color: #ffffff;
                    border-top: 1px solid #f1f5f9;
                }
                .social-links {
                    margin: 0 0 14px;
                    padding-bottom: 12px;
                    border-bottom: 1px solid #e2e8f0;
                }
                .social-links img {
                    max-width: 24px;
                    max-height: 24px;
                }
                .company-info {
                    margin: 0 0 10px;
                }
                .company-details {
                    margin-top: 10px;
                    padding-top: 10px;
                    border-top: 1px solid #e2e8f0;
                }
                @media only screen and (max-width: 600px) {
                    .email-wrapper {
                        padding: 0;
                    }
                    .email-container {
                        border-radius: 0;
                    }
                    .email-header,
                    .email-content,
                    .social-section {
                        padding-left: 14px;
                        padding-right: 14px;
                    }
                    .main-heading {
                        font-size: 22px;
                    }
                    .cta-button {
                        width: 100%;
                        max-width: 320px;
                    }
                }
            </style>
        </head>
        <body>
            <div class="email-wrapper">
                <div class="email-container">
                    <div class="email-header">
                        {{LOGO_URL}}
                    </div>
                    <div class="email-content">
                        <h2 class="main-heading">{{MAIN_HEADING}}</h2>
                        <p class="greeting">{{GREETING}}</p>
                        <p class="message">{{MESSAGE}}</p>
                        <div class="cta-container">
                            <a href="{{ACTION_URL}}" class="cta-button">{{BUTTON_TEXT}}</a>
                        </div>
                        <div class="features-section">
                            <div class="features-title">{{SECONDARY_MESSAGE}}</div>
                            {{FEATURES}}
                        </div>
                        <p class="highlight-note">{{EXPIRY_INFO}}</p>
                        <p class="ignore-note">{{IGNORE_TEXT}}</p>
                        <div class="footer">
                            <p class="footer-text">{{FOOTER_BEST_REGARDS}}</p>
                            <p class="footer-team">{{FOOTER_TEAM_NAME}}</p>
                        </div>
                    </div>

                    <div class="social-section">
                        <div class="social-links">
                            {{SOCIAL_MEDIA_LINKS}}
                        </div>

                        <div class="company-info">
                            <p class="footer-text">
                                {{FOOTER_TAGLINE}}
                            </p>
                        </div>

                        <div class="company-details">
                            <p class="footer-text" style="font-size: 12px; margin: 4px 0;">
                                {{FOOTER_COPYRIGHT}}<br>
                                {{COMPANY_ADDRESS}}
                            </p>
                            <p class="footer-text" style="font-size: 12px; margin: 6px 0;">
                                <a href="{{UNSUBSCRIBE_URL}}" style="color: #9ca3af; text-decoration: underline;">{{UNSUBSCRIBE_TEXT}}</a>
                            </p>
                        </div>
                    </div>
                </div>
            </div>
        </body>
        </html>
        """;
}
