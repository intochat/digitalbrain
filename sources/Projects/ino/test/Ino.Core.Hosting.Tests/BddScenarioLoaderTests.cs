using Ino.Core.Hosting.Llm;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class BddScenarioLoaderTests
{
    [Fact]
    public void LoadFromString_extracts_scenario_prompt_and_reply_from_quoted_steps()
    {
        var yaml = """
            Feature: Example
              Scenario: A simple prompt
                Given the user says "hello world"
                Then the assistant replies "hi there"
            """;

        var result = BddScenarioLoader.LoadFromString(yaml, "example.feature");

        Assert.Single(result.Scenarios);
        var s = result.Scenarios[0];
        Assert.Equal("Example", s.FeatureTitle);
        Assert.Equal("A simple prompt", s.ScenarioName);
        Assert.Equal("hello world", s.PromptPattern);
        Assert.Equal("hi there", s.ReplyText);
    }

    [Fact]
    public void LoadFromString_skips_scenarios_missing_quoted_steps()
    {
        var yaml = """
            Feature: Mixed
              Scenario: Missing quotes
                Given the user says something
                Then the assistant replies something
              Scenario: Proper shape
                Given the user says "do the thing"
                Then the assistant replies "did the thing"
            """;

        var result = BddScenarioLoader.LoadFromString(yaml, "mixed.feature");

        var only = Assert.Single(result.Scenarios);
        Assert.Equal("Proper shape", only.ScenarioName);
        Assert.Contains(result.SkippedReasons, r => r.Contains("Missing quotes"));
    }

    [Fact]
    public void LoadFromString_expands_scenario_outline_examples_into_one_scenario_per_row()
    {
        var yaml = """
            Feature: Outline

              Scenario Outline: <kind> lookup
                Given the user says "find <kind> in <city>"
                Then the assistant replies "searching <kind> in <city>"

                Examples:
                  | kind   | city  |
                  | hotel  | Bali  |
                  | flight | Tokyo |
            """;

        var result = BddScenarioLoader.LoadFromString(yaml, "outline.feature");

        Assert.Equal(2, result.Scenarios.Count);
        Assert.Contains(result.Scenarios, s =>
            s.PromptPattern == "find hotel in Bali" && s.ReplyText == "searching hotel in Bali");
        Assert.Contains(result.Scenarios, s =>
            s.PromptPattern == "find flight in Tokyo" && s.ReplyText == "searching flight in Tokyo");
    }

    [Fact]
    public void LoadFromDirectories_skips_non_existent_paths_without_throwing()
    {
        var result = BddScenarioLoader.LoadFromDirectories(new[] { "C:/definitely/not/here" });
        Assert.Empty(result.Scenarios);
        Assert.Empty(result.SkippedReasons);
    }

    [Fact]
    public void LoadFromDirectories_picks_up_feature_files_recursively()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"ino-bdd-{Guid.NewGuid():N}");
        var nested = Path.Combine(tmp, "sub");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(nested, "sample.feature"), """
                Feature: Nested
                  Scenario: Nested match
                    Given the user says "ping"
                    Then the assistant replies "pong"
                """);

            var result = BddScenarioLoader.LoadFromDirectories(new[] { tmp });

            var only = Assert.Single(result.Scenarios);
            Assert.Equal("ping", only.PromptPattern);
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }
}
