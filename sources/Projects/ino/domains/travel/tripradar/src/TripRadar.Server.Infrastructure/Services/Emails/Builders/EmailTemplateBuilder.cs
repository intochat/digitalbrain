using System.Net;
using TripRadar.Server.Infrastructure.Constants;

namespace TripRadar.Server.Infrastructure.Services.Emails.Builders;

public class EmailTemplateBuilder
{
    private readonly Dictionary<string, string> _placeholders = new();
    private string _template = string.Empty;

    public EmailTemplateBuilder WithTemplate(string template)
    {
        _template = template;
        return this;
    }

    public EmailTemplateBuilder WithTitle(string title)
    {
        _placeholders[EmailConstants.Placeholders.Title] = title;
        return this;
    }

    public EmailTemplateBuilder WithHeaderColor(string color)
    {
        _placeholders[EmailConstants.Placeholders.HeaderColor] = color;
        return this;
    }

    public EmailTemplateBuilder WithMainHeading(string heading)
    {
        _placeholders[EmailConstants.Placeholders.MainHeading] = heading;
        return this;
    }

    public EmailTemplateBuilder WithUsername(string username)
    {
        _placeholders[EmailConstants.Placeholders.Username] = WebUtility.HtmlEncode(username);
        return this;
    }

    public EmailTemplateBuilder WithMessage(string message)
    {
        _placeholders[EmailConstants.Placeholders.Message] = message;
        return this;
    }

    public EmailTemplateBuilder WithButtonText(string buttonText)
    {
        _placeholders[EmailConstants.Placeholders.ButtonText] = buttonText;
        return this;
    }

    public EmailTemplateBuilder WithButtonColor(string color)
    {
        _placeholders[EmailConstants.Placeholders.ButtonColor] = color;
        return this;
    }

    public EmailTemplateBuilder WithButtonHoverColor(string color)
    {
        _placeholders[EmailConstants.Placeholders.ButtonHoverColor] = color;
        return this;
    }

    public EmailTemplateBuilder WithActionUrl(string? url)
    {
        _placeholders[EmailConstants.Placeholders.ActionUrl] = ValidateAndSanitizeUrl(url) ?? EmailConstants.Fallbacks.NoActionUrl;
        return this;
    }

    public EmailTemplateBuilder WithSecondaryMessage(string message)
    {
        _placeholders[EmailConstants.Placeholders.SecondaryMessage] = message;
        return this;
    }

    public EmailTemplateBuilder WithFeatures(string features)
    {
        _placeholders[EmailConstants.Placeholders.Features] = features;
        return this;
    }

    public EmailTemplateBuilder WithExpiryInfo(string expiryInfo)
    {
        _placeholders[EmailConstants.Placeholders.ExpiryInfo] = expiryInfo;
        return this;
    }

    public EmailTemplateBuilder WithIgnoreText(string ignoreText)
    {
        _placeholders[EmailConstants.Placeholders.IgnoreText] = ignoreText;
        return this;
    }

    public EmailTemplateBuilder WithGreeting(string greeting)
    {
        _placeholders[EmailConstants.Placeholders.Greeting] = greeting;
        return this;
    }

    public EmailTemplateBuilder WithUrlFallbackLabel(string label)
    {
        _placeholders[EmailConstants.Placeholders.UrlFallbackLabel] = label;
        return this;
    }

    public EmailTemplateBuilder WithFooterBestRegards(string bestRegards)
    {
        _placeholders[EmailConstants.Placeholders.FooterBestRegards] = bestRegards;
        return this;
    }

    public EmailTemplateBuilder WithFooterTeamName(string teamName)
    {
        _placeholders[EmailConstants.Placeholders.FooterTeamName] = teamName;
        return this;
    }

    public EmailTemplateBuilder WithFooterCopyright(string copyright)
    {
        _placeholders[EmailConstants.Placeholders.FooterCopyright] = copyright;
        return this;
    }

    public EmailTemplateBuilder WithFooterTagline(string tagline)
    {
        _placeholders[EmailConstants.Placeholders.FooterTagline] = tagline;
        return this;
    }

    public EmailTemplateBuilder WithSocialMediaLinks(string socialLinks)
    {
        _placeholders[EmailConstants.Placeholders.SocialMediaLinks] = socialLinks;
        return this;
    }

    public EmailTemplateBuilder WithCompanyAddress(string address)
    {
        _placeholders[EmailConstants.Placeholders.CompanyAddress] = address;
        return this;
    }

    public EmailTemplateBuilder WithUnsubscribeUrl(string url)
    {
        _placeholders[EmailConstants.Placeholders.UnsubscribeUrl] = ValidateAndSanitizeUrl(url) ?? EmailConstants.Fallbacks.NoActionUrl;
        return this;
    }

    public EmailTemplateBuilder WithUnsubscribeText(string text)
    {
        _placeholders[EmailConstants.Placeholders.UnsubscribeText] = text;
        return this;
    }

    public EmailTemplateBuilder WithLogoUrl(string logoUrl)
    {
        _placeholders[EmailConstants.Placeholders.LogoUrl] = logoUrl ?? string.Empty;
        return this;
    }

    public EmailTemplateBuilder WithSignatureImageUrl(string signatureUrl)
    {
        _placeholders[EmailConstants.Placeholders.SignatureImageUrl] = signatureUrl ?? string.Empty;
        return this;
    }

    public string Build()
    {
        var result = _template;

        foreach (var (placeholder, value) in _placeholders)
        {
            result = result.Replace(placeholder, value);
        }

        return result;
    }

    public EmailTemplateBuilder Reset()
    {
        _placeholders.Clear();
        _template = string.Empty;
        return this;
    }

    private static string? ValidateAndSanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        return uri.AbsoluteUri;
    }
}

public static class EmailTemplateBuilderExtensions
{
    public static EmailTemplateBuilder CreateEmailTemplate(this EmailTemplateBuilder builder, string template)
    {
        return builder.Reset().WithTemplate(template);
    }
}
