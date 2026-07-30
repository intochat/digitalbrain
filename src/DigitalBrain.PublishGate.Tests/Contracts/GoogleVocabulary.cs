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
        "Google.Contracts public vocabulary is IGmail and GmailMessage only — ICalendar remains absent")]
    public void PublicVocabularyIsGmailOnly()
    {
        var contracts = typeof(IGmail).Assembly;

        var vocabulary = contracts
            .GetExportedTypes()
            .Where(type => type.Namespace == GoogleNamespace)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal([nameof(GmailMessage), nameof(IGmail)], vocabulary);
        Assert.Null(contracts.GetType($"{GoogleNamespace}.ICalendar"));
        Assert.DoesNotContain(
            contracts.GetExportedTypes(),
            type => type.Name is "ICalendar" or "Calendar" or "IGmailTool" or "McpServerDefinition");
    }

    [Fact(DisplayName =
        "IGmail.ReadMessage is unsuffixed, aliased, and returns GmailMessage")]
    public void ReadMessageIsUnsuffixedAliasedAndReturnsGmailMessage()
    {
        var methods = typeof(IGmail)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.Equal([nameof(IGmail.ReadMessage)], methods.Select(method => method.Name));
        Assert.All(methods, method =>
        {
            Assert.DoesNotContain("Async", method.Name, StringComparison.Ordinal);
            Assert.Equal(method.Name, method.GetCustomAttribute<AliasAttribute>()?.Alias);
            Assert.Equal(typeof(Task<GmailMessage>), method.ReturnType);
        });

        Assert.Contains(typeof(INeuron), typeof(IGmail).GetInterfaces());
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
