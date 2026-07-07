using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Tests.Aspire;

public sealed class OAuthCallbackPathTests
{
    [Fact]
    public void GoogleCallbackPathIsSharedAcrossAppHostAndRuntime()
    {
        Assert.Equal("/oauth/callback/google", OAuthCallbackPaths.Google);
        Assert.Equal(OAuthCallbackPaths.Google, GoogleAspireExtensions.DefaultCallbackPath);
        Assert.Equal(OAuthCallbackPaths.Google, GoogleClientFactory.DefaultCallbackPath);
        Assert.EndsWith(OAuthCallbackPaths.Google, GoogleClientFactory.DefaultRedirectUri);
    }

    [Fact]
    public void SalesforceCallbackPathIsSharedAcrossAppHostAndRuntime()
    {
        Assert.Equal("/oauth/callback/salesforce", OAuthCallbackPaths.Salesforce);
        Assert.Equal(OAuthCallbackPaths.Salesforce, SalesforceAspireExtensions.DefaultCallbackPath);
        Assert.Equal(OAuthCallbackPaths.Salesforce, SalesforceClientFactory.DefaultCallbackPath);
        Assert.EndsWith(OAuthCallbackPaths.Salesforce, SalesforceClientFactory.DefaultRedirectUri);
    }
}
