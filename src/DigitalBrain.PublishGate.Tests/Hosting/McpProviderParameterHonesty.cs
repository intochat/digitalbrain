using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Google;
using DigitalBrain.Google.Aspire.Hosting;
using DigitalBrain.Salesforce;
using DigitalBrain.Salesforce.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests.Hosting;

public sealed class McpProviderParameterHonesty
{
    private static readonly string[] ForbiddenPlaceholders =
    [
        "local-dev",
        "local-dev-secret",
        "http://localhost/oauth/callback",
    ];

    private static readonly string[] ProviderParameterNames =
    [
        "google-client-id",
        "google-client-secret",
        "google-redirect-uri",
        "salesforce-client-id",
        "salesforce-redirect-uri",
    ];

    [Fact(DisplayName =
        "run-mode MCP provider parameters do not default to local-dev placeholders")]
    public async Task RunModeProviderParametersHaveNoPlaceholderDefaults()
    {
        var builder = DistributedApplication.CreateBuilder();
        Assert.True(
            builder.ExecutionContext.IsRunMode,
            "CreateBuilder() must exercise the run-mode parameter path.");

        var brain = builder.AddDigitalBrain("brain");
        brain.AddModule<GoogleModule>(google => google.WithGmail());
        brain.AddModule<SalesforceModule>(salesforce => salesforce.WithSalesforce());

        var parameters = builder.Resources
            .OfType<ParameterResource>()
            .ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

        foreach (var name in ProviderParameterNames)
        {
            Assert.True(parameters.ContainsKey(name), $"Expected parameter '{name}' to be registered.");
            await AssertParameterIsRequiredWithoutPlaceholder(parameters[name]).ConfigureAwait(true);
        }

        Assert.True(
            parameters.ContainsKey("mcp-authorization-mode"),
            "Expected mcp-authorization-mode to be registered.");
        await AssertParameterIsRequiredWithoutPlaceholder(parameters["mcp-authorization-mode"])
            .ConfigureAwait(true);
    }

    private static async Task AssertParameterIsRequiredWithoutPlaceholder(ParameterResource parameter)
    {
        if (parameter.Default is not null)
        {
            var defaultValue = parameter.Default.GetDefaultValue();
            foreach (var placeholder in ForbiddenPlaceholders)
            {
                Assert.False(
                    string.Equals(defaultValue, placeholder, StringComparison.Ordinal),
                    $"Parameter '{parameter.Name}' must not default to placeholder '{placeholder}'.");
            }
        }

        try
        {
            var resolved = await parameter.GetValueAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var placeholder in ForbiddenPlaceholders)
            {
                Assert.False(
                    string.Equals(resolved, placeholder, StringComparison.Ordinal),
                    $"Parameter '{parameter.Name}' must not resolve to placeholder '{placeholder}'.");
            }
        }
        catch (MissingParameterValueException)
        {
        }
    }
}
