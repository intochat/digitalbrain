using System.Text;
using System.Text.Json;
using DigitalBrain.Features.Sdk;
using DigitalBrain.Integrations.Google.Contracts;
using DigitalBrain.Integrations.Salesforce.Contracts;
using DigitalBrain.Integrations.Web.Contracts;
namespace DigitalBrain.Features.EnrichSalesforce;

public sealed class EnrichSalesforceFeature : IFeature
{
    private const string InputKind = "gmail.message.received.v1";
    private const string DescriptionField = "Description";
    private const string UpdateOperationKey = "enrich-salesforce-description";
    private readonly IGmailMessageReader _gmail;
    private readonly IWebSearchReader _web;
    private readonly ISalesforceAccountSearcher _accounts;
    private readonly ISalesforceUpdateProposer _updates;

    public EnrichSalesforceFeature(
        IGmailMessageReader gmail,
        IWebSearchReader web,
        ISalesforceAccountSearcher accounts,
        ISalesforceUpdateProposer updates)
    {
        _gmail = gmail ?? throw new ArgumentNullException(nameof(gmail));
        _web = web ?? throw new ArgumentNullException(nameof(web));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _updates = updates ?? throw new ArgumentNullException(nameof(updates));
    }

    public async Task HandleAsync(FeatureInput input, IFeatureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(input.Kind, InputKind, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unsupported Feature input kind: {input.Kind}.", nameof(input));
        }
        if (!input.Facts.TryGetValue("messageId", out var messageId))
        {
            throw new ArgumentException("Feature input requires messageId.", nameof(input));
        }

        var message = await _gmail.ReadAsync(new GmailMessageReadRequest(messageId), cancellationToken);
        var company = CompanyName(message.SenderAddress ?? string.Empty);
        var matches = await _accounts.SearchAsync(new SalesforceAccountSearchRequest(company, 2), cancellationToken);
        if (matches.Accounts.Count == 0)
        {
            throw new InvalidOperationException($"No Salesforce account matched {company}.");
        }
        if (matches.Accounts.Count != 1)
        {
            throw new InvalidOperationException($"Salesforce account matching for {company} is ambiguous.");
        }

        var evidence = await _web.SearchAsync(new WebSearchRequest($"{company} company overview", 3), cancellationToken);
        var result = evidence.Results.FirstOrDefault() ?? throw new InvalidOperationException($"No public web evidence was found for {company}.");
        var description = BoundedDescription(message, result);
        await _updates.ProposeAsync(
            new SalesforceUpdateProposalRequest(
                matches.Accounts[0].Record,
                DescriptionField,
                JsonSerializer.SerializeToElement(description),
                UpdateOperationKey),
            cancellationToken);
    }

    private static string CompanyName(string senderAddress)
    {
        var at = senderAddress.LastIndexOf('@');
        if (at <= 0 || at == senderAddress.Length - 1)
        {
            throw new InvalidOperationException("The Gmail sender address cannot identify a company.");
        }
        var domain = senderAddress[(at + 1)..].Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (domain.Length == 0)
        {
            throw new InvalidOperationException("The Gmail sender address cannot identify a company.");
        }
        string[] suffixes = ["robotics", "technologies", "technology", "systems", "software", "labs"];
        var suffix = suffixes.FirstOrDefault(candidate =>
            domain.Length > candidate.Length &&
            domain.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
        var words = suffix is null
            ? [domain]
            : new[] { domain[..^suffix.Length], suffix };
        return string.Join(' ', words.Select(static word =>
            char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));
    }

    private static string BoundedDescription(GmailMessage message, WebSearchResult evidence)
    {
        var value = $"DigitalBrain enrichment from Gmail \"{message.Subject}\". Sender: {message.SenderAddress}. Public evidence: {evidence.Snippet} Source: {evidence.Url}";
        if (Encoding.UTF8.GetByteCount(value) <= 4_000)
        {
            return value;
        }
        var builder = new StringBuilder();
        var bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > 4_000)
            {
                break;
            }
            builder.Append(rune);
            bytes += rune.Utf8SequenceLength;
        }
        return builder.ToString();
    }
}
