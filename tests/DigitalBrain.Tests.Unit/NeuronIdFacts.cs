using System.Text.Json;

namespace DigitalBrain.Tests.Unit;

public sealed class NeuronIdFacts
{
    [Fact]
    public void Parse_splits_on_the_first_two_separators()
    {
        var id = NeuronId.Parse("b1/files/folder/a/b");

        Assert.Equal(new NeuronId(new BrainId("b1"), "files", "folder/a/b"), id);
        Assert.Equal("b1/files/folder/a/b", id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("b1")]
    [InlineData("b1/files")]
    [InlineData("b1/files/")]
    [InlineData("b1//key")]
    [InlineData("b:1/files/key")]
    [InlineData("@kernel/files/key")]
    [InlineData("@other/files/key")]
    public void Parse_rejects_malformed_ids(string text)
    {
        var error = Assert.Throws<FormatException>(() => NeuronId.Parse(text));
        Assert.Contains("brain/type/key", error.Message, StringComparison.Ordinal);
        Assert.False(NeuronId.TryParse(text, out _));
    }

    [Fact]
    public void Kernel_ids_print_but_do_not_parse_from_user_text()
    {
        var id = new NeuronId(BrainId.Kernel, "ledger", "main");

        Assert.Equal("@kernel/ledger/main", id.ToString());
        Assert.False(NeuronId.TryParse(id.ToString(), out _));
    }

    [Fact]
    public void Constructor_rejects_empty_type_or_key()
    {
        Assert.Throws<ArgumentException>(() => new NeuronId(new BrainId("b1"), "", "k"));
        Assert.Throws<ArgumentException>(() => new NeuronId(new BrainId("b1"), "a/b", "k"));
        Assert.Throws<ArgumentException>(() => new NeuronId(new BrainId("b1"), "t", ""));
    }

    [Fact]
    public void Json_uses_the_string_form()
    {
        var id = new NeuronId(new BrainId("b1"), "files", "folder/a");

        var json = JsonSerializer.Serialize(id);

        Assert.Equal("\"b1/files/folder/a\"", json);
        Assert.Equal(id, JsonSerializer.Deserialize<NeuronId>(json));
    }
}
