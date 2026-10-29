using System.Reflection;
using System.Text.Json;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Tests.Unit;

public sealed class CapabilityIdFacts
{
    [Fact]
    public void Parse_reads_name_at_major()
    {
        Assert.Equal(new CapabilityId("storage", 1), CapabilityId.Parse("storage@1"));
    }

    [Fact]
    public void ToString_round_trips()
    {
        var id = new CapabilityId("mail-send", 12);
        Assert.Equal("mail-send@12", id.ToString());
        Assert.Equal(id, CapabilityId.Parse(id.ToString()));
        Assert.Equal(new CapabilityId("google.drive", 1), CapabilityId.Parse("google.drive@1"));
    }

    [Theory]
    [InlineData("storage")]
    [InlineData("storage@0")]
    [InlineData("storage@01")]
    [InlineData("storage@x")]
    [InlineData("@1")]
    [InlineData("Storage@1")]
    [InlineData("storage@-1")]
    [InlineData("1storage@1")]
    [InlineData("storage@1@2")]
    [InlineData("")]
    public void Parse_rejects_malformed_text(string text)
    {
        var error = Assert.Throws<FormatException>(() => CapabilityId.Parse(text));
        Assert.Contains($"'{text}'", error.Message, StringComparison.Ordinal);
        Assert.False(CapabilityId.TryParse(text, out _));
    }

    [Fact]
    public void Parse_rejects_names_over_64_characters()
    {
        var name64 = "a" + new string('b', 63);
        Assert.Equal(name64, CapabilityId.Parse(name64 + "@1").Name);
        Assert.Throws<FormatException>(() => CapabilityId.Parse(name64 + "b@1"));
    }

    [Fact]
    public void Constructor_rejects_bad_names_and_majors()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityId("Bad Name", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapabilityId("ok", -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapabilityId("ok", 0));
    }

    [Fact]
    public void Satisfies_needs_the_same_name_and_major()
    {
        Assert.True(new CapabilityId("storage", 1).Satisfies(new CapabilityId("storage", 1)));
        Assert.False(new CapabilityId("storage", 2).Satisfies(new CapabilityId("storage", 1)));
        Assert.False(new CapabilityId("secrets", 1).Satisfies(new CapabilityId("storage", 1)));
    }

    [Fact]
    public void Json_uses_the_string_form()
    {
        var json = JsonSerializer.Serialize(new[] { Capability.Storage.V1 });
        Assert.Equal("[\"storage@1\"]", json);
        Assert.Equal([Capability.Storage.V1], JsonSerializer.Deserialize<CapabilityId[]>(json)!);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CapabilityId>("\"storage\""));
    }

    [Fact]
    public void Capability_constants_are_storage_secrets_identity_v1()
    {
        Assert.Equal("storage@1", Capability.Storage.V1.ToString());
        Assert.Equal("secrets@1", Capability.Secrets.V1.ToString());
        Assert.Equal("identity@1", Capability.Identity.V1.ToString());
        Assert.Equal(Capability.Storage.V1Id, Capability.Storage.V1.ToString());
        Assert.Equal(Capability.Secrets.V1Id, Capability.Secrets.V1.ToString());
        Assert.Equal(Capability.Identity.V1Id, Capability.Identity.V1.ToString());

        var nested = typeof(Capability).GetNestedTypes(BindingFlags.Public).Select(type => type.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["Identity", "Secrets", "Storage"], nested);
    }

    [Fact]
    public void Capability_attribute_exposes_its_id()
    {
        Assert.Equal(new CapabilityId("storage", 1), new CapabilityAttribute("storage", 1).Id);
    }
}
