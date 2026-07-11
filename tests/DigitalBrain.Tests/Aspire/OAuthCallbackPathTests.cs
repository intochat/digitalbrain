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
        Assert.Equal("/oauth/start/salesforce", OAuthCallbackPaths.SalesforceStart);
        Assert.Equal(OAuthCallbackPaths.Salesforce, SalesforceAspireExtensions.DefaultCallbackPath);
        Assert.Equal(OAuthCallbackPaths.Salesforce, SalesforceClientFactory.DefaultCallbackPath);
        Assert.EndsWith(OAuthCallbackPaths.Salesforce, SalesforceClientFactory.DefaultRedirectUri);
    }

    [Fact]
    public void SalesforceStartUrlAllowsOnlyBoundedLocalOrHttpsTokenLinks()
    {
        Assert.True(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "http://localhost:8081/oauth/start/salesforce?t=opaque-token"));
        Assert.False(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "https://brain.example/oauth/start/salesforce?t=opaque-token"));
        Assert.True(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "https://brain.example/oauth/start/salesforce?t=opaque-token",
            "https://brain.example/oauth/callback/salesforce"));
        Assert.False(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "https://evil.example/oauth/start/salesforce?t=opaque-token",
            "https://brain.example/oauth/callback/salesforce"));
        Assert.False(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "http://brain.example/oauth/start/salesforce?t=opaque-token"));
        Assert.False(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "https://brain.example/oauth/start/salesforce?t=opaque-token&state=provider-state"));
        Assert.False(OAuthCallbackPaths.IsAllowedSalesforceStartUrl(
            "https://login.salesforce.com/services/oauth2/authorize?t=opaque-token"));
    }
}
