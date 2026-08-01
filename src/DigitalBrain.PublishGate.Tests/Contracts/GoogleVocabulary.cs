using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Google;
using Xunit;

namespace DigitalBrain.Tests.Contracts;

public sealed class GoogleVocabulary
{
    private static readonly string GoogleNamespace =
        typeof(IGmail).Namespace
        ?? throw new InvalidOperationException($"{nameof(IGmail)} has no namespace.");

    [Fact(DisplayName =
        "Google.Contracts public vocabulary is IGmail, GmailMessage, GmailRequest, and GmailResponse — ICalendar remains absent")]
    public void PublicVocabularyIsGmailOnly()
    {
        var contracts = typeof(IGmail).Assembly;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == GoogleNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [nameof(GmailMessage), nameof(GmailRequest), nameof(GmailResponse), nameof(IGmail)],
            vocabulary);
        Assert.Null(contracts.GetType($"{GoogleNamespace}.ICalendar"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes(),
            type => type.Name is "ICalendar" or "Calendar" or "IGmailTool" or "McpServerDefinition");
    }

    [Fact(DisplayName =
        "IGmail is a marker INeuron with no declared operation methods")]
    public void IGmailIsMarkerWithNoOperationMethods()
    {
        var methods = typeof(IGmail)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Empty(methods);
        Assert.Contains(typeof(INeuron), typeof(IGmail).GetInterfaces());
    }

    [Fact(DisplayName =
        "GmailRequest is an intent-level RequestSynapse of GmailResponse")]
    public void GmailRequestIsIntentRequestSynapse()
    {
        Assert.True(typeof(RequestSynapse<GmailResponse>).IsAssignableFrom(typeof(GmailRequest)));

        var alias = typeof(GmailRequest)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-request", alias);

        var properties = typeof(GmailRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailRequest.CommandId),
                nameof(GmailRequest.Intent),
            ],
            properties);
    }

    [Fact(DisplayName =
        "GmailMessage wire alias and fields are the public email vocabulary")]
    public void GmailMessageWireAliasAndFields()
    {
        var alias = typeof(GmailMessage)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();

        Assert.Equal("db.google.gmail-message", alias);

        var properties = typeof(GmailMessage)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailMessage.Id),
                nameof(GmailMessage.PlaintextBody),
                nameof(GmailMessage.Sender),
                nameof(GmailMessage.Subject),
            ],
            properties);
    }

    [Fact(DisplayName =
        "Google runtime public surface is GoogleModule only — no public tool or OAuth types")]
    public void RuntimePublicSurfaceIsModuleMarkerOnly()
    {
        var exported = typeof(GoogleModule).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(GoogleModule)], exported);
        Assert.DoesNotContain(
            typeof(GoogleModule).Assembly.GetExportedTypes(),
            type => type.Name.Contains("Tool", StringComparison.Ordinal)
                || type.Name.Contains("OAuth", StringComparison.Ordinal)
                || type.Name.Contains("Mcp", StringComparison.Ordinal));
    }
}
