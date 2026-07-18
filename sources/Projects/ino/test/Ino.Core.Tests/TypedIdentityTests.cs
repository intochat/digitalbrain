using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public class TypedIdentityTests
{
    [Fact]
    public void DomainId_From_rejects_null_or_whitespace()
    {
        Assert.Throws<ArgumentException>(() => DomainId.From("  "));
    }

    [Fact]
    public void DomainId_From_preserves_value()
    {
        Assert.Equal("Ino.Domains.Travel", DomainId.From("Ino.Domains.Travel").Value);
    }

    [Fact]
    public void DomainId_ToString_is_value()
    {
        Assert.Equal("Ino.Domains.Travel", DomainId.From("Ino.Domains.Travel").ToString());
    }

    [Fact]
    public void DomainId_equality_is_by_value()
    {
        Assert.Equal(DomainId.From("x"), DomainId.From("x"));
        Assert.NotEqual(DomainId.From("y"), DomainId.From("x"));
    }

    [Fact]
    public void SynapseId_New_produces_unique_values()
    {
        var a = SynapseId.New();
        var b = SynapseId.New();
        Assert.NotEqual(b, a);
        Assert.False(string.IsNullOrEmpty(a.Value));
    }

    [Fact]
    public void CorrelationId_New_produces_unique_values()
    {
        Assert.NotEqual(CorrelationId.New(), CorrelationId.New());
    }

    [Fact]
    public void EventId_New_produces_ulid_ordered_values()
    {
        var a = EventId.New();
        Thread.Sleep(2);
        var b = EventId.New();
        // Ulid ids sort lexicographically by creation time
        Assert.True(string.Compare(a.Value, b.Value, StringComparison.Ordinal) < 0);
    }

    [Fact]
    public void StreamKey_is_readonly_record_struct()
    {
        Assert.True(typeof(StreamKey).IsValueType);
    }
}
