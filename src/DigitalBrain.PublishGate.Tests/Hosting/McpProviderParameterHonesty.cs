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
    private static readonly string[] ForbiddenCredentialPlaceholders =
    [
        "local-dev",
        "local-dev-secret",
        "http://localhost/oauth/callback",
    ];

    [Fact(DisplayName =
        "run-mode MCP credentials stay required while OAuth callback defaults to fixed UI product URL")]
    public async Task RunModeCredentialsRequiredAndCallbackDefaultsToFixedUiPort()
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

        foreach (var name in new[]
                 {
                     "google-client-id",
                     "google-client-secret",
                     "salesforce-client-id",
                 })
        {
            Assert.True(parameters.ContainsKey(name), $"Expected parameter '{name}' to be registered.");
            await AssertCredentialHasNoFakeDefault(parameters[name]).ConfigureAwait(true);
        }

        Assert.False(
            parameters.ContainsKey("salesforce-client-secret"),
            "Salesforce must not register a client-secret parameter.");

        Assert.True(parameters.ContainsKey("google-redirect-uri"));
        Assert.True(parameters.ContainsKey("salesforce-redirect-uri"));
        Assert.Equal(
            LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri,
            await ResolveDefaultOrValue(parameters["google-redirect-uri"]).ConfigureAwait(true));
        Assert.Equal(
            LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri,
            await ResolveDefaultOrValue(parameters["salesforce-redirect-uri"]).ConfigureAwait(true));

        Assert.True(parameters.ContainsKey("mcp-authorization-mode"));
        Assert.Equal(
            LocalDevelopmentProductSurface.LocalLoopbackAuthorizationMode,
            await ResolveDefaultOrValue(parameters["mcp-authorization-mode"]).ConfigureAwait(true));
    }

    [Fact(DisplayName = "Flutter UI edge binds the stable local OAuth callback host port")]
    public void FlutterUiEdgeBindsStableLocalPort()
    {
        Assert.Equal(5080, LocalDevelopmentProductSurface.UiHttpPort);
        Assert.Equal(
            "http://localhost:5080/oauth/mcp/callback",
            LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri);
    }

    private static async Task AssertCredentialHasNoFakeDefault(ParameterResource parameter)
    {
        if (parameter.Default is not null)
        {
            var defaultValue = parameter.Default.GetDefaultValue();
            foreach (var placeholder in ForbiddenCredentialPlaceholders)
            {
                Assert.False(
                    string.Equals(defaultValue, placeholder, StringComparison.Ordinal),
                    $"Parameter '{parameter.Name}' must not default to placeholder '{placeholder}'.");
            }

            Assert.False(
                string.Equals(
                    defaultValue,
                    LocalDevelopmentProductSurface.LocalDevelopmentOAuthCallbackUri,
                    StringComparison.Ordinal),
                $"Credential parameter '{parameter.Name}' must not default to the OAuth callback URL.");
        }

        try
        {
            var resolved = await parameter.GetValueAsync(CancellationToken.None).ConfigureAwait(true);
            foreach (var placeholder in ForbiddenCredentialPlaceholders)
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

    private static async Task<string?> ResolveDefaultOrValue(ParameterResource parameter)
    {
        if (parameter.Default is not null)
        {
            return parameter.Default.GetDefaultValue();
        }

        try
        {
            return await parameter.GetValueAsync(CancellationToken.None).ConfigureAwait(true);
        }
        catch (MissingParameterValueException)
        {
            return null;
        }
    }
}
