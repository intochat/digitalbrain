using DigitalBrain.Aspire;
using DigitalBrain.Core;
using DigitalBrain.Google;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Tests.Aspire;

public sealed class OAuthCallbackPathTests
{
    private const string FlowReference = "abcdefghijklmnopqrstuvwxyzABCDEF0123456789-_";

    [Fact]
    public void GoogleCallbackPathIsSharedAcrossAppHostAndRuntime()
    {
        Assert.Equal("/oauth/callback/google", OAuthCallbackPaths.Google);
        Assert.Equal("/oauth/start/google", OAuthCallbackPaths.GoogleStart);
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
        Assert.Equal(
            "http://localhost:51014/oauth/callback/salesforce",
            SalesforceClientFactory.DefaultRedirectUri);
    }

    [Theory]
    [InlineData(OAuthCallbackPaths.GoogleProvider, OAuthCallbackPaths.GoogleStart)]
    [InlineData(OAuthCallbackPaths.SalesforceProvider, OAuthCallbackPaths.SalesforceStart)]
    public void InternalStartPathsUseOneCanonicalOpaqueFlowReference(string provider, string expectedPath)
    {
        var target = OAuthCallbackPaths.CreateInternalStartPath(provider, FlowReference);

        Assert.Equal($"{expectedPath}?f={FlowReference}", target);
        Assert.True(OAuthCallbackPaths.TryParseInternalStartPath(target, provider, out var parsed));
        Assert.Equal(FlowReference, parsed);
        Assert.True(OAuthCallbackPaths.TryParseInternalStartPath(target, out var parsedProvider, out parsed));
        Assert.Equal(provider, parsedProvider);
        Assert.Equal(FlowReference, parsed);
    }

    [Fact]
    public void InternalStartPathsRejectNonCanonicalAndProviderTargets()
    {
        var invalidTargets = new[]
        {
            $"https://brain.example{OAuthCallbackPaths.GoogleStart}?f={FlowReference}",
            $"{OAuthCallbackPaths.GoogleStart}?t={FlowReference}",
            $"{OAuthCallbackPaths.GoogleStart}?f={FlowReference}&state=provider-state",
            $"{OAuthCallbackPaths.GoogleStart}?f={FlowReference}#fragment",
            $"{OAuthCallbackPaths.GoogleStart}?f={FlowReference}=",
            $"{OAuthCallbackPaths.GoogleStart}?f=short",
            $"/oauth/start/Google?f={FlowReference}",
            "https://accounts.google.com/o/oauth2/v2/auth?state=provider-state",
            "https://login.salesforce.com/services/oauth2/authorize?state=provider-state"
        };

        foreach (var target in invalidTargets)
            Assert.False(OAuthCallbackPaths.TryParseInternalStartPath(target, out _, out _));

        var googleTarget = OAuthCallbackPaths.CreateInternalStartPath(
            OAuthCallbackPaths.GoogleProvider,
            FlowReference);
        Assert.False(OAuthCallbackPaths.TryParseInternalStartPath(
            googleTarget,
            OAuthCallbackPaths.SalesforceProvider,
            out _));
    }

    [Fact]
    public void FlowReferencesAreBase64UrlAndBounded()
    {
        Assert.True(OAuthCallbackPaths.IsOpaqueFlowReference(
            new string('a', OAuthCallbackPaths.MinimumFlowReferenceLength)));
        Assert.True(OAuthCallbackPaths.IsOpaqueFlowReference(
            new string('a', OAuthCallbackPaths.MaximumFlowReferenceLength)));
        Assert.False(OAuthCallbackPaths.IsOpaqueFlowReference(
            new string('a', OAuthCallbackPaths.MinimumFlowReferenceLength - 1)));
        Assert.False(OAuthCallbackPaths.IsOpaqueFlowReference(
            new string('a', OAuthCallbackPaths.MaximumFlowReferenceLength + 1)));
        Assert.False(OAuthCallbackPaths.IsOpaqueFlowReference(FlowReference + "+"));
        Assert.Throws<ArgumentException>(() => OAuthCallbackPaths.CreateInternalStartPath(
            OAuthCallbackPaths.GoogleProvider,
            "short"));
        Assert.Throws<ArgumentOutOfRangeException>(() => OAuthCallbackPaths.CreateInternalStartPath(
            "unknown",
            FlowReference));
    }

    [Theory]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=test", true)]
    [InlineData("http://accounts.google.com/o/oauth2/v2/auth?client_id=test", false)]
    [InlineData("https://accounts.google.com:444/o/oauth2/v2/auth?client_id=test", false)]
    [InlineData("https://accounts.google.com/o/oauth2/auth?client_id=test", false)]
    [InlineData("https://accounts.google.com.evil.example/o/oauth2/v2/auth?client_id=test", false)]
    [InlineData("https://accounts.google.com@evil.example/o/oauth2/v2/auth?client_id=test", false)]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=test#fragment", false)]
    public void GoogleProviderRedirectAllowlistIsExact(string target, bool expected)
    {
        Assert.Equal(expected, GoogleClientFactory.IsAllowedAuthorizationUrl(target));
    }

    [Fact]
    public void ProviderRedirectAllowlistIsBoundToTheExpectedProvider()
    {
        const string google = "https://accounts.google.com/o/oauth2/v2/auth?client_id=test";
        const string salesforce = "https://login.salesforce.com/services/oauth2/authorize?client_id=test";

        Assert.True(OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl(
            OAuthCallbackPaths.GoogleProvider,
            google));
        Assert.True(OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl(
            OAuthCallbackPaths.SalesforceProvider,
            salesforce));
        Assert.False(OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl(
            OAuthCallbackPaths.GoogleProvider,
            salesforce));
        Assert.False(OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl(
            OAuthCallbackPaths.SalesforceProvider,
            google));
        Assert.False(OAuthCallbackPaths.IsAllowedProviderAuthorizationUrl("unknown", google));
    }
}
