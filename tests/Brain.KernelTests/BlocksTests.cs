using System.Text.Json;
using Brain.Contracts;
using Brain.Modules.Workspace;
using Xunit;

namespace Brain.KernelTests;

public class BlocksTests
{
    [Fact]
    public void Doc_round_trips_through_parse()
    {
        var doc = Blocks.Doc(
            Blocks.Text("hello"),
            Blocks.Metric("Score", 42),
            Blocks.Section("Details", Blocks.Text("nested")));

        var parsed = BlockDoc.Parse(doc.Json);

        Assert.Equal(doc.Json, parsed.Json);

        using var json = JsonDocument.Parse(parsed.Json);
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var blocks = root.GetProperty("blocks");
        Assert.Equal(3, blocks.GetArrayLength());
        Assert.Equal("text", blocks[0].GetProperty("kind").GetString());
        Assert.Equal("metric", blocks[1].GetProperty("kind").GetString());
        Assert.Equal("section", blocks[2].GetProperty("kind").GetString());
    }

    [Fact]
    public void Unknown_kind_is_rejected()
    {
        var json = """{"version":1,"blocks":[{"kind":"bogus"}]}""";

        var exception = Assert.Throws<BrainException>(() => BlockDoc.Parse(json));

        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Nesting_beyond_max_depth_is_rejected()
    {
        var block = Blocks.Text("leaf");
        for (var i = 0; i < 9; i++)
            block = Blocks.Section($"level {i}", block);
        var doc = Blocks.Doc(block);

        var exception = Assert.Throws<BrainException>(() => BlockDoc.Parse(doc.Json));

        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Oversized_doc_is_rejected()
    {
        var doc = Blocks.Doc(Blocks.Text(new string('a', BlockDoc.MaxBytes)));

        var exception = Assert.Throws<BrainException>(() => BlockDoc.Parse(doc.Json));

        Assert.Equal("input.invalid", exception.Code);
    }

    [Fact]
    public void Action_row_preserves_label_contract_and_input_json()
    {
        var doc = Blocks.Doc(Blocks.ActionRow(Blocks.Action("Approve", "workspace.approve.v1", "{\"id\":7}")));

        var parsed = BlockDoc.Parse(doc.Json);

        using var json = JsonDocument.Parse(parsed.Json);
        var action = json.RootElement.GetProperty("blocks")[0].GetProperty("actions")[0];
        Assert.Equal("Approve", action.GetProperty("label").GetString());
        Assert.Equal("workspace.approve.v1", action.GetProperty("contract").GetString());
        Assert.Equal("{\"id\":7}", action.GetProperty("inputJson").GetString());
    }
}
