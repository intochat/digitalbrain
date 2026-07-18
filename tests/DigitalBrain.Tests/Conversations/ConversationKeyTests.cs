using System.Text;
using DigitalBrain;
using Xunit;

namespace DigitalBrain.Tests.Conversations;

public sealed class ConversationKeyTests
{
    [Fact]
    public void Encode_produces_exactly_versioned_base64url_owner_and_conversation_segments()
    {
        var owner = new BrainOwnerId("owner-a");
        var conversation = new ConversationId("chat/main");

        var key = ConversationKey.Encode(owner, conversation);

        var segments = key.Split('.');
        Assert.Equal(3, segments.Length);
        Assert.Equal("v1", segments[0]);
        Assert.Equal(Base64Url(owner.Value), segments[1]);
        Assert.Equal(Base64Url(conversation.Value), segments[2]);
    }

    [Fact]
    public void Round_trip_preserves_owner_and_conversation()
    {
        var owner = new BrainOwnerId("owner-a");
        var conversation = new ConversationId("chat.main.v1");

        Assert.True(ConversationKey.TryParse(
            ConversationKey.Encode(owner, conversation),
            out var parsedOwner,
            out var parsedConversation));
        Assert.Equal(owner, parsedOwner);
        Assert.Equal(conversation, parsedConversation);
    }

    [Fact]
    public void Unicode_owner_round_trips_canonically()
    {
        var owner = new BrainOwnerId("uživatel-東京-😀");
        var conversation = new ConversationId("chat-😀");
        var key = ConversationKey.Encode(owner, conversation);

        Assert.True(ConversationKey.TryParse(key, out var parsedOwner, out var parsedConversation));
        Assert.Equal(owner, parsedOwner);
        Assert.Equal(conversation, parsedConversation);
        Assert.Equal(key, ConversationKey.Encode(parsedOwner, parsedConversation));
    }

    [Fact]
    public void Invalid_utf16_cannot_collapse_distinct_key_components()
    {
        var conversation = new ConversationId("chat");
        var owner = new BrainOwnerId("owner-a");

        Assert.ThrowsAny<ArgumentException>(() =>
            ConversationKey.Encode(new BrainOwnerId("\uD800"), conversation));
        Assert.ThrowsAny<ArgumentException>(() =>
            ConversationKey.Encode(owner, new ConversationId("\uD800")));
    }

    [Fact]
    public void Owners_with_delimiter_like_values_cannot_forge_each_other()
    {
        var forgeryPairs = new[]
        {
            (new BrainOwnerId("owner"), new ConversationId("a.b")),
            (new BrainOwnerId("owner.a"), new ConversationId("b"))
        };

        var keys = forgeryPairs
            .Select(pair => ConversationKey.Encode(pair.Item1, pair.Item2))
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        foreach (var (key, pair) in keys.Zip(forgeryPairs))
        {
            Assert.True(ConversationKey.TryParse(key, out var owner, out var conversation));
            Assert.Equal(pair.Item1, owner);
            Assert.Equal(pair.Item2, conversation);
        }
    }

    [Fact]
    public void Same_conversation_id_for_different_owners_produces_different_keys()
    {
        var conversation = new ConversationId("shared-name");

        Assert.NotEqual(
            ConversationKey.Encode(new BrainOwnerId("owner-a"), conversation),
            ConversationKey.Encode(new BrainOwnerId("owner-b"), conversation));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v1")]
    [InlineData("v1.b25seQ")]
    [InlineData("v1.b3duZXI.Y2hhdA.ZXh0cmE")]
    [InlineData("v2.b3duZXI.Y2hhdA")]
    [InlineData("v1..Y2hhdA")]
    [InlineData("v1.b3duZXI.")]
    [InlineData("v1.not+base64url.Y2hhdA")]
    [InlineData("v1.b3duZXI=.Y2hhdA")]
    [InlineData("owner-a")]
    public void Malformed_keys_are_rejected(string malformedKey)
    {
        Assert.False(ConversationKey.TryParse(malformedKey, out _, out _));
    }

    [Fact]
    public void Non_canonical_encodings_of_a_valid_key_are_rejected()
    {
        var canonical = ConversationKey.Encode(new BrainOwnerId("owner-a"), new ConversationId("chat"));
        var segments = canonical.Split('.');
        var nonCanonicalOwner = segments[1] + "==";

        Assert.False(ConversationKey.TryParse($"v1.{nonCanonicalOwner}.{segments[2]}", out _, out _));
    }

    [Fact]
    public void Decoded_segments_are_revalidated()
    {
        var forgedConversation = Base64Url("   ");
        var owner = Base64Url("owner-a");

        Assert.False(ConversationKey.TryParse($"v1.{owner}.{forgedConversation}", out _, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("has\ncontrol")]
    public void Encode_rejects_invalid_owner(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            ConversationKey.Encode(new BrainOwnerId(value), new ConversationId("chat")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("has\ncontrol")]
    public void Decoded_invalid_owner_is_rejected(string value)
    {
        var conversation = Base64Url("chat");

        Assert.False(ConversationKey.TryParse(
            $"v1.{Base64Url(value)}.{conversation}",
            out _,
            out _));
    }

    private static string Base64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
