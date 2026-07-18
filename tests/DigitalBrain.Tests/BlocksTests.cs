using System.Text.Json;
using Brain.Contracts;
using Flutter.Contracts;
using Xunit;

namespace Brain.KernelTests;

public sealed class BlocksTests
{
    [Fact]
    public void Ui_document_round_trips_through_parse()
    {
        var document = new UiDocument(1, [
            new UiBlock("heading", Text: "Summary"),
            new UiBlock("text", Text: "hello"),
            new UiBlock("status", Label: "Connection", Value: "ready")
        ]);
        var json = JsonSerializer.Serialize(document, JsonSerializerOptions.Web);

        var parsed = UiDocument.Parse(json);

        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(JsonSerializer.Serialize(parsed, JsonSerializerOptions.Web));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Fact]
    public void Button_action_preserves_contract_target_and_input()
    {
        var json = """
            {"version":1,"blocks":[{"kind":"button","label":"Approve","action":{"contract":"effect.approve.v1","target":"owner|actor/test|effect/1","inputJson":"{}"}}]}
            """;

        var document = UiDocument.Parse(json);
        var action = Assert.Single(document.Blocks).Action;

        Assert.NotNull(action);
        Assert.Equal("effect.approve.v1", action.Contract);
        Assert.Equal("owner|actor/test|effect/1", action.Target);
        Assert.Equal("{}", action.InputJson);
    }

    [Fact]
    public void Unsupported_kind_is_rejected()
    {
        var exception = Assert.Throws<BrainException>(() =>
            UiDocument.Parse("""{"version":1,"blocks":[{"kind":"metric"}]}"""));

        Assert.Equal("input.invalid", exception.Code);
    }
}
