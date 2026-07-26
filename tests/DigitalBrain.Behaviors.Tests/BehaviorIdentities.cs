namespace DigitalBrain.Behaviors.Tests;

using System.Reflection;
using DigitalBrain.Abstractions;
using Xunit;

public sealed class BehaviorIdentities
{
    [Theory(DisplayName = "BehaviorId accepts canonical DNS-style names")]
    [InlineData("com.digitalbrain.start-ui")]
    [InlineData("community.alice.mail-triage")]
    public void BehaviorIdsAcceptCanonicalDnsStyleNames(string value)
        => Assert.Equal(value, BehaviorId.Parse(value).Value);

    [Theory(DisplayName = "BehaviorId rejects noncanonical names before they become durable identities")]
    [InlineData("StartUi")]
    [InlineData("two..dots")]
    [InlineData("space here")]
    [InlineData("ab")]
    [InlineData("a..b")]
    [InlineData("a.-b")]
    [InlineData("a.b-")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void BehaviorIdsRejectNonCanonicalNames(string value)
        => Assert.Throws<FormatException>(() => BehaviorId.Parse(value));

    [Theory(DisplayName = "BehaviorRevisionId accepts only lowercase SHA-256 digests")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void BehaviorRevisionIdsAcceptCanonicalSha256Digests(string value)
        => Assert.Equal(value, BehaviorRevisionId.Parse(value).Value);

    [Theory(DisplayName = "BehaviorRevisionId rejects noncanonical digest strings before approval")]
    [InlineData("0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde")]
    [InlineData("g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void BehaviorRevisionIdsRejectNonCanonicalSha256Digests(string value)
        => Assert.Throws<FormatException>(() => BehaviorRevisionId.Parse(value));

    [Fact(DisplayName = "Behavior execution identity is Orleans-serialized and rejects an empty GUID")]
    public void BehaviorExecutionIdentityIsStableAndNonEmpty()
    {
        var value = new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a");
        var execution = new BehaviorExecutionId(value);

        Assert.Equal(value, execution.Value);
        Assert.NotNull(typeof(BehaviorExecutionId).GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal("db.behavior-execution-id", typeof(BehaviorExecutionId).GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.Throws<ArgumentException>(() => new BehaviorExecutionId(Guid.Empty));
    }

    [Theory(DisplayName = "Behavior identities retain stable Orleans aliases and field identifiers")]
    [InlineData(typeof(BehaviorId), "db.behavior-id")]
    [InlineData(typeof(BehaviorRevisionId), "db.behavior-revision-id")]
    [InlineData(typeof(BehaviorExecutionId), "db.behavior-execution-id")]
    public void BehaviorIdentitiesKeepStableSerializationMetadata(Type type, string alias)
    {
        ArgumentNullException.ThrowIfNull(type);

        Assert.NotNull(type.GetCustomAttribute<GenerateSerializerAttribute>());
        Assert.Equal(alias, type.GetCustomAttribute<AliasAttribute>()?.Alias);
        Assert.NotNull(Assert.Single(type.GetProperties()).GetCustomAttribute<IdAttribute>());
    }
}
