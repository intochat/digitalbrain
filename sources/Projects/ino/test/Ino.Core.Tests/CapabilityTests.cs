using System.Collections.Immutable;
using Xunit;

namespace Ino.Core.Tests;

public sealed class CapabilityTests
{
    [Fact]
    public void Http_CarriesAllowedHosts()
    {
        var cap = new Capability.Http("serpapi.com", "*.airlines");

        Assert.Equal(new[] { "serpapi.com", "*.airlines" }, cap.AllowedHosts);
    }

    [Fact]
    public void Llm_DefaultsToBalancedTier()
    {
        var cap = new Capability.Llm();

        Assert.Equal(LlmTier.Balanced, cap.Tier);
    }

    [Fact]
    public void Llm_CanRequestReasoningTier()
    {
        var cap = new Capability.Llm(LlmTier.Reasoning);

        Assert.Equal(LlmTier.Reasoning, cap.Tier);
    }

    [Fact]
    public void Identity_CarriesProviderAndScopes()
    {
        var cap = new Capability.Identity("google.com", "email", "profile");

        Assert.Equal("google.com", cap.Provider);
        Assert.Equal(new[] { "email", "profile" }, cap.Scopes);
    }

    [Fact]
    public void Persistence_CarriesStoragePrefix()
    {
        var cap = new Capability.Persistence("trip-planner");

        Assert.Equal("trip-planner", cap.StoragePrefix);
    }

    [Fact]
    public void TwoHttpWithSameHosts_AreEqual()
    {
        var a = new Capability.Http("example.com");
        var b = new Capability.Http("example.com");

        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void TwoHttpWithDifferentHosts_AreNotEqual()
    {
        var a = new Capability.Http("example.com");
        var b = new Capability.Http("other.com");

        Assert.NotEqual(b, a);
    }

    [Fact]
    public void EmptyHttp_HashCode_IsNonZero()
    {
        // Regression test for empty-array hash-code degeneracy — Aggregate
        // with seed 0 previously returned 0 for every empty Http, causing
        // dictionary collisions. Seeded with typeof(Http).GetHashCode().
        var cap = new Capability.Http();

        Assert.NotEqual(0, cap.GetHashCode());
    }

    [Fact]
    public void TwoIdentityWithSameProviderAndScopes_AreEqual()
    {
        var a = new Capability.Identity("google.com", "email", "profile");
        var b = new Capability.Identity("google.com", "email", "profile");

        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void TwoIdentityWithDifferentProvider_AreNotEqual()
    {
        var a = new Capability.Identity("google.com", "email");
        var b = new Capability.Identity("microsoft.com", "email");

        Assert.NotEqual(b, a);
    }

    [Fact]
    public void TwoIdentityWithDifferentScopes_AreNotEqual()
    {
        var a = new Capability.Identity("google.com", "email");
        var b = new Capability.Identity("google.com", "email", "profile");

        Assert.NotEqual(b, a);
    }

    [Fact]
    public void Http_with_no_hosts_equals_other_empty()
    {
        var a = new Capability.Http();
        var b = new Capability.Http();
        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void Http_with_null_params_is_empty_not_NRE()
    {
        string[]? hosts = null;
        var cap = new Capability.Http(hosts);
        Assert.Empty(cap.AllowedHosts);
    }

    [Fact]
    public void Identity_with_no_scopes_equals_other_same_provider()
    {
        var a = new Capability.Identity("google.com");
        var b = new Capability.Identity("google.com");
        Assert.Equal(b, a);
        Assert.Equal(b.GetHashCode(), a.GetHashCode());
    }

    [Fact]
    public void Http_AllowedHosts_is_ImmutableArray()
    {
        var cap = new Capability.Http("a", "b");
        Assert.IsType<ImmutableArray<string>>(cap.AllowedHosts);
        Assert.Equal(new[] { "a", "b" }, cap.AllowedHosts);
    }
}
