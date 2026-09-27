using DigitalBrain.Specs;
using Xunit;

namespace DigitalBrain.Modules.Specs.Tests.Unit;

public sealed class GherkinParserFacts
{
    [Fact]
    public void ParsesBackgroundScenariosTagsAndDocStrings()
    {
        var feature = GherkinParser.Parse(""""
            # comment
            Feature: Group chat
              Two models talk.

              Background:
                Given the app is installed

              @smoke
              Scenario: They agree
                When I ask "Name an idea"
                Then the answer mentions "dog"
                And the model replies:
                  """
                  first line
                    indented
                  """
            """");

        Assert.Equal("Group chat", feature.Name);
        Assert.Equal("the app is installed", Assert.Single(feature.Background).Text);
        var scenario = Assert.Single(feature.Scenarios);
        Assert.Equal("They agree", scenario.Name);
        Assert.Equal(["@smoke"], scenario.Tags);
        Assert.Equal(["When", "Then", "And"], scenario.Steps.Select(step => step.Keyword));
        Assert.Equal("first line\n  indented", scenario.Steps[2].DocString);
        Assert.Equal(10, scenario.Steps[0].Line);
    }

    [Theory]
    [InlineData("Scenario: no feature\n  Given x", 1)]
    [InlineData("Feature: f\nScenario: s\n  Given x\n  something odd", 4)]
    [InlineData("Feature: f\nScenario Outline: s\n  Given x", 2)]
    [InlineData("Feature: f\nScenario: empty", 2)]
    [InlineData("Feature: f\nScenario: s\n  Given x\n  \"\"\"\n  never closed", 4)]
    public void ReportsTheLineOfEveryProblem(string text, int line)
        => Assert.Equal(line, Assert.Throws<SpecSyntaxException>(() => GherkinParser.Parse(text)).Line);
}
