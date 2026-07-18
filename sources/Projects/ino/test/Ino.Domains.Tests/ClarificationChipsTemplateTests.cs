using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.UI;
using Xunit;

namespace Ino.Domains.Tests;

public sealed class ClarificationChipsTemplateTests
{
    [Fact]
    public void Build_emits_LF_only_description_with_imports_and_event_binding()
    {
        var (description, _) = ClarificationChipsTemplate.Build(
            prompt: "When are you going?",
            field: "dates",
            suggestions: ["this weekend", "next week", "next month"]);

        var text = Encoding.UTF8.GetString(description);
        Assert.DoesNotContain("\r", text);
        Assert.Contains("import core.widgets;", text);
        Assert.Contains("widget root = Column(", text);
        Assert.Contains("event \"ino:provide-clarification\"", text);
        Assert.Contains("data.suggestions.0", text);
        Assert.Contains("data.suggestions.1", text);
        Assert.Contains("data.suggestions.2", text);
    }

    [Fact]
    public void Build_emits_data_payload_with_prompt_field_and_suggestions()
    {
        var (_, data) = ClarificationChipsTemplate.Build(
            prompt: "Where would you like to go?",
            field: "destination",
            suggestions: ["Tokyo", "Paris", "NYC"]);

        var json = Encoding.UTF8.GetString(data);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Where would you like to go?", root.GetProperty("prompt").GetString());
        Assert.Equal("destination", root.GetProperty("field").GetString());
        var suggestions = root.GetProperty("suggestions");
        Assert.Equal(3, suggestions.GetArrayLength());
        Assert.Equal("Tokyo", suggestions[0].GetString());
        Assert.Equal("Paris", suggestions[1].GetString());
        Assert.Equal("NYC", suggestions[2].GetString());
    }

    [Fact]
    public void Build_with_empty_suggestions_still_emits_valid_description()
    {
        var (description, _) = ClarificationChipsTemplate.Build("Any preferences?", "freeform", []);
        var text = Encoding.UTF8.GetString(description);
        Assert.Contains("Wrap(", text);
        // Empty Wrap children list — no chip lines, no trailing comma issue
        Assert.DoesNotContain("data.suggestions.0", text);
    }

    [Fact]
    public void EventName_is_kernel_namespaced()
    {
        Assert.Equal("ino:provide-clarification", ClarificationChipsTemplate.EventName);
    }
}
