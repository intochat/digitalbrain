using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceClientFactoryTests
{
    [Fact]
    public void TokenEndpoint_Appends_OAuth_Token_Path()
    {
        var endpoint = SalesforceClientFactory.TokenEndpoint("https://test.salesforce.com/");

        Assert.Equal("https://test.salesforce.com/services/oauth2/token", endpoint);
    }

    [Fact]
    public async Task CreateForceClientAsync_Missing_Config_Throws_Clear_Error()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SalesforceClientFactory.CreateForceClientAsync(new Dictionary<string, string>()));

        Assert.Contains("missing client_id", ex.Message);
        Assert.Contains("Salesforce", ex.Message);
    }
}
