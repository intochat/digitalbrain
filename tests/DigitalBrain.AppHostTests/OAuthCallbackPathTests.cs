using DigitalBrain.AppHost;
using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.AppHostTests;

public sealed class OAuthCallbackPathTests
{
    private const string FlowReference = "abcdefghijklmnopqrstuvwxyzABCDEF0123456789-_";

    [Fact]
    public void GoogleCallbackPathIsSharedAcrossAppHostAndRuntime()
    {
        Assert.Equal("/oauth/callback/google", GoogleAspireExtensions.DefaultCallbackPath);
    }

    [Fact]
    public void SalesforceCallbackPathIsSharedAcrossAppHostAndRuntime()
    {
        Assert.Equal("/oauth/callback/salesforce", SalesforceAspireExtensions.DefaultCallbackPath);
    }

    [Theory]
    [InlineData("google", "/oauth/start/google")]
    [InlineData("salesforce", "/oauth/start/salesforce")]
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
            $"https://brain.example/oauth/start/google?f={FlowReference}",
            $"/oauth/start/google?t={FlowReference}",
            $"/oauth/start/google?f={FlowReference}&state=provider-state",
            $"/oauth/start/google?f={FlowReference}#fragment",
            $"/oauth/start/google?f={FlowReference}=",
            "/oauth/start/google?f=short",
            $"/oauth/start/Google?f={FlowReference}",
            "https://accounts.google.com/o/oauth2/v2/auth?state=provider-state",
            "https://login.salesforce.com/services/oauth2/authorize?state=provider-state"
        };

        foreach (var target in invalidTargets)
            Assert.False(OAuthCallbackPaths.TryParseInternalStartPath(target, out _, out _));

        var googleTarget = OAuthCallbackPaths.CreateInternalStartPath(
            "google",
            FlowReference);
        Assert.False(OAuthCallbackPaths.TryParseInternalStartPath(
            googleTarget,
            "salesforce",
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
            "google",
            "short"));
        Assert.Throws<ArgumentException>(() => OAuthCallbackPaths.CreateInternalStartPath(
            "Not-Canonical",
            FlowReference));
    }
}
