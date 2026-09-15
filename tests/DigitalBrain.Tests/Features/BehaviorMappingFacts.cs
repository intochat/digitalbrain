using System.Text.Json;
using DigitalBrain.Core.Behaviors;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorMappingFacts
{
    [Theory]
    [InlineData("""{"properties":{"value":{"type":"string"}},"required":["value"]}""")]
    [InlineData("""{"type":["object","null"],"properties":{"value":{"type":"string"}},"required":["value"]}""")]
    public void MappingRejectsInputWhoseRequiredFieldsOnlyApplyToObjects(string schema)
    {
        using var template = JsonDocument.Parse("\"$input.value\"");
        Assert.Throws<ArgumentException>(() => BehaviorMapping.InferSchema(template.RootElement, schema));
    }

    [Fact]
    public void MappingRequiresEveryTraversedParentToBeAnObject()
    {
        const string schema = """{"type":"object","properties":{"nested":{"type":["object","null"],"properties":{"value":{"type":"string"}},"required":["value"]}},"required":["nested"]}""";
        using var template = JsonDocument.Parse("\"$input.nested.value\"");
        Assert.Throws<ArgumentException>(() => BehaviorMapping.InferSchema(template.RootElement, schema));
    }

    [Theory]
    [InlineData("9007199254740993", "9007199254740992", "greaterThan", true)]
    [InlineData("9007199254740992", "9007199254740993", "lessThan", true)]
    [InlineData("1.000000000000000000000000000001", "1", "greaterThan", true)]
    [InlineData("1e10000", "9e9999", "greaterThan", true)]
    [InlineData("-1e10000", "-9e9999", "lessThan", true)]
    [InlineData("1e-10000", "0", "greaterThan", true)]
    [InlineData("-0e10000", "0", "lessThan", false)]
    [InlineData("1.0", "10e-1", "greaterThan", false)]
    [InlineData("100", "99.999", "greaterThan", true)]
    public void NumericFiltersCompareExactDecimalValues(string actual, string expected, string operation, bool match)
    {
        using var configuration = JsonDocument.Parse("{\"path\":\"value\",\"operator\":\"" + operation + "\",\"value\":" + expected + "}");
        const string schema = """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}""";
        BehaviorMapping.ValidateFilter(configuration.RootElement, schema);
        Assert.Equal(match, BehaviorMapping.Matches(configuration.RootElement, "{\"value\":" + actual + "}"));
    }

    [Fact]
    public void NumericFilterSizeLimitAppliesBeforeStartAndAtRuntime()
    {
        var number = "1" + new string('0', 4096);
        const string schema = """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}""";
        using var largeConfiguration = JsonDocument.Parse("{\"path\":\"value\",\"operator\":\"greaterThan\",\"value\":" + number + "}");
        Assert.Throws<ArgumentException>(() => BehaviorMapping.ValidateFilter(largeConfiguration.RootElement, schema));
        using var configuration = JsonDocument.Parse("""{"path":"value","operator":"greaterThan","value":0}""");
        Assert.Throws<ArgumentException>(() => BehaviorMapping.Matches(configuration.RootElement, "{\"value\":" + number + "}"));
    }

    [Fact]
    public void RequiredReferenceFieldsRemainAssignableAcrossSchemaRoots()
    {
        const string output = """{"$defs":{"value":{"type":"integer"}},"type":"object","properties":{"value":{"$ref":"#/$defs/value"}},"required":["value"],"additionalProperties":false}""";
        const string input = """{"$defs":{"value":{"type":"number"}},"type":"object","properties":{"value":{"$ref":"#/$defs/value"}},"required":["value"]}""";
        Assert.True(BehaviorSchema.IsAssignable(output, input));
        Assert.False(BehaviorSchema.IsAssignable(input, output));
        Assert.Empty(BehaviorSchema.Validate(output, """{"value":42}"""));
        Assert.NotEmpty(BehaviorSchema.Validate(output, """{"value":"42"}"""));
    }
}
