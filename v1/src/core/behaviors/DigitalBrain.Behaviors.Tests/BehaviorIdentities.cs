namespace DigitalBrain.Behaviors.Tests;

using System.Reflection;
using System.Text.Json;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
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

    [Fact(DisplayName = "Uninitialized behavior identities reject identity operations before they can become durable keys")]
    public void DefaultBehaviorIdentitiesRejectIdentityOperations()
    {
        var validBehavior = new BehaviorId("com.digitalbrain.start-ui");
        var validRevision = new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        var validExecution = new BehaviorExecutionId(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a"));

        Assert.Throws<InvalidOperationException>(() => default(BehaviorId).ToString());
        Assert.Throws<InvalidOperationException>(() => default(BehaviorRevisionId).ToString());
        Assert.Throws<InvalidOperationException>(() => default(BehaviorExecutionId).ToString());
        Assert.Throws<InvalidOperationException>(() => default(BehaviorId).Equals(validBehavior));
        Assert.Throws<InvalidOperationException>(() => default(BehaviorRevisionId).Equals(validRevision));
        Assert.Throws<InvalidOperationException>(() => default(BehaviorExecutionId).Equals(validExecution));
    }

    [Fact(DisplayName = "Behavior identities remain value-equal and usable as durable dictionary keys")]
    public void BehaviorIdentitiesKeepValueEqualityAndDictionarySemantics()
    {
        var left = new BehaviorId("com.digitalbrain.start-ui");
        var right = new BehaviorId("com.digitalbrain.start-ui");
        var values = new Dictionary<BehaviorId, string> { [left] = "approved" };

        Assert.Equal(left, right);
        Assert.Equal("approved", values[right]);
    }

    [Fact(DisplayName = "Behavior identity JSON invokes validating constructors and preserves valid values")]
    public void BehaviorIdentityJsonRoundTripsAndRejectsMalformedValues()
    {
        var behavior = new BehaviorId("com.digitalbrain.start-ui");
        var revision = new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef");
        var execution = new BehaviorExecutionId(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a"));

        Assert.Equal(behavior, JsonSerializer.Deserialize<BehaviorId>(JsonSerializer.Serialize(behavior)));
        Assert.Equal(revision, JsonSerializer.Deserialize<BehaviorRevisionId>(JsonSerializer.Serialize(revision)));
        Assert.Equal(execution, JsonSerializer.Deserialize<BehaviorExecutionId>(JsonSerializer.Serialize(execution)));
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<BehaviorId>("{\"Value\":\"StartUi\"}"));
        Assert.Throws<FormatException>(() => JsonSerializer.Deserialize<BehaviorRevisionId>("{\"Value\":\"g123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\"}"));
        Assert.Throws<ArgumentException>(() => JsonSerializer.Deserialize<BehaviorExecutionId>("{\"Value\":\"00000000-0000-0000-0000-000000000000\"}"));
    }

    [Fact(DisplayName = "Behavior identities and execution metadata retain their Orleans wire values")]
    public void BehaviorIdentityOrleansRoundTripsPreserveValues()
    {
        var metadata = new BehaviorExecutionMetadata(
            new OwnerId("owner"),
            new BehaviorId("com.digitalbrain.start-ui"),
            new BehaviorRevisionId("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"),
            new BehaviorExecutionId(new Guid("4b050fe8-45d0-4a16-b6a5-1b4b6683880a")));

        Assert.Equal(metadata, OrleansRoundTrip(metadata));
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
        Assert.Equal(0u, Assert.Single(type.GetProperties()).GetCustomAttribute<IdAttribute>()?.Id);
    }

    private static T OrleansRoundTrip<T>(T value)
    {
        using var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer<T>>();
        return serializer.Deserialize(serializer.SerializeToArray(value));
    }
}
