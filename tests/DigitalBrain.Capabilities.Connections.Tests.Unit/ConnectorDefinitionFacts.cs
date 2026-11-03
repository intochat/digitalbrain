using DigitalBrain.Capabilities.Connections.Connectors;

namespace DigitalBrain.Capabilities.Connections.Tests.Unit;

public sealed class ConnectorDefinitionFacts
{
    private sealed class SampleModule
    {
        public static ConnectorDefinition[] Connectors { get; } =
        [
            new ConnectorDefinition("gmail", "Gmail", ["google"], ["google-oauth"])
        ];
    }

    [Fact]
    public void DeclaredByReadsAStaticConnectorsProperty()
    {
        var connectors = ConnectorDefinition.DeclaredBy(typeof(SampleModule));
        var connector = Assert.Single(connectors);
        Assert.Equal("gmail", connector.Id);
    }

    [Fact]
    public void OAuthMethodMustNameADeclaredIntegration()
    {
        var undeclared = new ConnectorDefinition("gmail", "Gmail", ["google"], [])
        {
            OAuthMethods =
            [
                new OAuthMethod(
                    "google",
                    "https://accounts.google.com/o/oauth2/v2/auth",
                    "https://oauth2.googleapis.com/token",
                    ["openid"],
                    "google-oauth")
            ]
        };

        var ex = Assert.Throws<InvalidOperationException>(() => undeclared.Validate());
        Assert.Contains("google-oauth", ex.Message, StringComparison.Ordinal);

        var declaredRequired = undeclared with
        {
            RequiredIntegrations = ["google-oauth"]
        };
        declaredRequired.Validate();

        var declaredOptional = undeclared with
        {
            OptionalIntegrations = ["google-oauth"]
        };
        declaredOptional.Validate();
    }
}
