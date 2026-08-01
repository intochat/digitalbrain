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
        "Google.Contracts public vocabulary is IGmail plus intent and typed Gmail ops — ICalendar remains absent")]
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
            [
                nameof(GmailGetMessageRequest),
                nameof(GmailGetMessageResponse),
                nameof(GmailMessage),
                nameof(GmailMessageHeader),
                nameof(GmailRequest),
                nameof(GmailResponse),
                nameof(GmailSearchRequest),
                nameof(GmailSearchResponse),
                nameof(IGmail),
            ],
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
        "GmailSearchRequest is a read-only bounded RequestSynapse of GmailSearchResponse")]
    public void GmailSearchRequestIsTypedOpRequestSynapse()
    {
        Assert.True(typeof(RequestSynapse<GmailSearchResponse>).IsAssignableFrom(typeof(GmailSearchRequest)));

        var alias = typeof(GmailSearchRequest)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-search-request", alias);

        var properties = typeof(GmailSearchRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailSearchRequest.CommandId),
                nameof(GmailSearchRequest.MaxResults),
                nameof(GmailSearchRequest.Query),
            ],
            properties);
    }

    [Fact(DisplayName =
        "GmailSearchResponse wire alias, headers list, and Succeeded track Error null")]
    public void GmailSearchResponseWireAliasAndSucceeded()
    {
        var alias = typeof(GmailSearchResponse)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-search-response", alias);

        var properties = typeof(GmailSearchResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailSearchResponse.CommandId),
                nameof(GmailSearchResponse.Error),
                nameof(GmailSearchResponse.Headers),
                nameof(GmailSearchResponse.Succeeded),
            ],
            properties);
    }

    [Fact(DisplayName =
        "GmailMessageHeader is metadata-only with no body field")]
    public void GmailMessageHeaderIsMetadataOnly()
    {
        var alias = typeof(GmailMessageHeader)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-message-header", alias);

        var properties = typeof(GmailMessageHeader)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailMessageHeader.Id),
                nameof(GmailMessageHeader.Sender),
                nameof(GmailMessageHeader.Subject),
            ],
            properties);
        Assert.DoesNotContain("PlaintextBody", properties);
        Assert.DoesNotContain("Body", properties);
    }

    [Fact(DisplayName =
        "GmailGetMessageRequest is a read-only RequestSynapse of GmailGetMessageResponse")]
    public void GmailGetMessageRequestIsTypedOpRequestSynapse()
    {
        Assert.True(typeof(RequestSynapse<GmailGetMessageResponse>).IsAssignableFrom(typeof(GmailGetMessageRequest)));

        var alias = typeof(GmailGetMessageRequest)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-get-message-request", alias);

        var properties = typeof(GmailGetMessageRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailGetMessageRequest.CommandId),
                nameof(GmailGetMessageRequest.MessageId),
            ],
            properties);
    }

    [Fact(DisplayName =
        "GmailGetMessageResponse wire alias carries optional GmailMessage and Error")]
    public void GmailGetMessageResponseWireAliasAndShape()
    {
        var alias = typeof(GmailGetMessageResponse)
            .GetCustomAttributes<AliasAttribute>(inherit: false)
            .Select(attribute => attribute.Alias)
            .Single();
        Assert.Equal("db.google.gmail-get-message-response", alias);

        var properties = typeof(GmailGetMessageResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(GmailGetMessageResponse.CommandId),
                nameof(GmailGetMessageResponse.Error),
                nameof(GmailGetMessageResponse.Message),
                nameof(GmailGetMessageResponse.Succeeded),
            ],
            properties);
    }

    [Fact(DisplayName =
        "Google.Contracts assembly references neither Google.Apis nor ModelContextProtocol assemblies")]
    public void ContractsAssemblyReferencesNoGoogleApisOrModelContextProtocol()
    {
        var referenced = typeof(IGmail).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith("Google.Apis", StringComparison.Ordinal));
        Assert.DoesNotContain(
            referenced,
            name => name.StartsWith("ModelContextProtocol", StringComparison.Ordinal));
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
