using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.OS;
using Xunit;

namespace DigitalBrain.OS.Behaviors.Tests;

public sealed class AssistantHardCodedCapabilities
{
    [Fact(DisplayName = "assistant source no longer hard-codes provider enrichment tools or fixed account names")]
    public void AssistantDoesNotHardCodeProviderTools()
    {
        var assistantType = typeof(OSBehaviorsModule).Assembly.GetType("DigitalBrain.OS.Assistant");
        Assert.NotNull(assistantType);

        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "os", "DigitalBrain.OS.Behaviors", "Assistant.cs"));

        Assert.DoesNotContain("enrich_account_from_email", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EnrichAccountFromEmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultGmailAccount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultSalesforceAccount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ProposeEnrichmentAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationToken.None", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISalesforce", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GmailRequest", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SalesforceRequest", source, StringComparison.Ordinal);

        Assert.Null(assistantType.GetField("EnrichAccountFromEmail", BindingFlags.Public | BindingFlags.Static));
        Assert.True(typeof(Agent).IsAssignableFrom(assistantType));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }
}
