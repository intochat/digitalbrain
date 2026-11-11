using DigitalBrain.Screens;

namespace DigitalBrain.Analyzers.Tests.Unit.Screens;

public sealed class SpecCommentParserFacts
{
    private const string Calculator = """
        # Calculator

        Role: Example
        Shipped: true

        A keypad that evaluates arithmetic.

        ## Scenario: Opening shows the keypad

        Invoking "open" presents the keypad.

        ## Scenario: Clear from the dock

        The toolbar posts "clear"; the display becomes empty.

        ## Scenario: Add answers a sum

        Invoking "add" with "2+3" sets the sum to "5".
        """;

    [Fact]
    public void Parses_title_role_shipped_description_scenarios_and_invoked_operations()
    {
        var spec = SpecComment.Parse(Calculator);
        Assert.Empty(spec.Errors);
        Assert.Equal("Calculator", spec.Title);
        Assert.Equal(AppRole.Example, spec.Role);
        Assert.True(spec.Shipped);
        Assert.Equal("A keypad that evaluates arithmetic.", spec.Description);
        Assert.Equal(["Opening shows the keypad", "Clear from the dock", "Add answers a sum"], spec.Scenarios);
        Assert.False(spec.OperationsDeclared);
        Assert.Equal(["add", "clear", "open"], spec.Operations.Order(StringComparer.Ordinal));
        Assert.True(spec.Describes("Open"));
        Assert.True(spec.Describes("Clear"));
        Assert.False(spec.Describes("Multiply"));
    }

    [Theory]
    [InlineData("Shell", (int)AppRole.Shell)]
    [InlineData("Default", (int)AppRole.Default)]
    [InlineData("Optional", (int)AppRole.Optional)]
    [InlineData("Example", (int)AppRole.Example)]
    public void Every_role_parses(string text, int role)
    {
        var spec = SpecComment.Parse("# A\n\nRole: " + text + "\nShipped: false\n");
        Assert.Empty(spec.Errors);
        Assert.Equal((AppRole)role, spec.Role);
        Assert.False(spec.Shipped);
    }

    [Theory]
    [InlineData("Role: Admin\n", 2, "Role is not one of")]
    [InlineData("Role: example\n", 2, "Role is not one of")]
    [InlineData("Role: Shell\nRole: Default\n", 3, "Role is declared twice")]
    [InlineData("Shipped: yes\n", 2, "Shipped is not true or false")]
    public void Unknown_or_repeated_headers_are_errors_naming_the_line_never_defaults(string headers, int line, string message)
    {
        var spec = SpecComment.Parse("# A\n" + headers);
        var error = Assert.Single(spec.Errors);
        Assert.Equal(line, error.Line);
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Absent_headers_are_null_without_errors()
    {
        var spec = SpecComment.Parse("# A\n\nJust prose.");
        Assert.Null(spec.Role);
        Assert.Null(spec.Shipped);
        Assert.Empty(spec.Errors);
    }

    [Fact]
    public void Operations_header_is_explicit_and_normalized()
    {
        var spec = SpecComment.Parse("# A\nOperations: open, Add-Item, `clear_all`\n\nInvoking \"other\" is prose here.");
        Assert.True(spec.OperationsDeclared);
        Assert.Equal(["open", "additem", "clearall"], spec.Operations);
        Assert.True(spec.Describes("AddItem"));
        Assert.False(spec.Describes("other"));
    }

    [Fact]
    public void Headers_inside_sections_are_prose()
    {
        var spec = SpecComment.Parse("# A\n\n## Scenario: x\n\nRole: Admin\n");
        Assert.Null(spec.Role);
        Assert.Empty(spec.Errors);
    }

    [Fact]
    public void Text_over_64_KiB_is_cut_and_reported()
    {
        var spec = SpecComment.Parse("# A\nRole: Shell\n" + new string('x', SpecComment.MaxLength + 10));
        Assert.Equal(SpecComment.MaxLength, spec.Text.Length);
        Assert.Equal(AppRole.Shell, spec.Role);
        Assert.Contains(spec.Errors, e => e.Line == 0);
    }

    [Fact]
    public void Extract_reads_leading_block_and_line_comments_and_stops_at_the_first_token()
    {
        Assert.Equal("# A\nRole: Shell", SpecComment.Extract("/*\n# A\nRole: Shell\n*/\nusing X;\n/* later */"));
        Assert.Equal("# A\nRole: Shell", SpecComment.Extract("﻿// # A\n// Role: Shell\nusing X;"));
        Assert.Null(SpecComment.Extract("using X;\n/* not leading */"));
        Assert.Null(SpecComment.Extract("/* unterminated"));
        Assert.Null(SpecComment.Extract("/*   */ class A {}"));
    }
}
