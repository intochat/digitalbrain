using System.Reflection;
using DigitalBrain.AI;
using DigitalBrain.OS.Assistant;
using Reqnroll;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

[Binding]
public sealed class CapabilitySteps
{
    private Type? _assistantType;

    [Given("the product assistant type")]
    public void GivenTheProductAssistantType()
    {
        _assistantType = typeof(AssistantModule).Assembly.GetType("DigitalBrain.OS.Assistant.Assistant");
        Assert.NotNull(_assistantType);
    }

    [Then("the assistant declares no hard-coded Gmail or Salesforce tool surface")]
    public void ThenAssistantDeclaresNoHardCodedProviderSurface()
    {
        Assert.NotNull(_assistantType);
        Assert.True(typeof(Agent).IsAssignableFrom(_assistantType));

        var source = File.ReadAllText(
            Path.Combine(RepoRoot(), "os", "DigitalBrain.OS.Assistant", "Assistant.cs"));

        Assert.DoesNotContain("enrich_account_from_email", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IGmail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ISalesforce", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultGmailAccount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefaultSalesforceAccount", source, StringComparison.Ordinal);
        Assert.Null(_assistantType!.GetMethod(
            "ToolsFor",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
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
