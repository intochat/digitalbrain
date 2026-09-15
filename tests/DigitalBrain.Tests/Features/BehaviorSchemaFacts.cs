using DigitalBrain.Core.Behaviors;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class BehaviorSchemaFacts
{
    private const string Person = """{"type":"object","properties":{"name":{"type":"string"},"age":{"type":"integer"}},"required":["name"],"additionalProperties":false}""";

    [Theory]
    [InlineData("{\"name\":\"Ada\",\"age\":36}", true)]
    [InlineData("{\"name\":\"Ada\"}", true)]
    [InlineData("{}", false)]
    [InlineData("{\"name\":7}", false)]
    [InlineData("{\"name\":\"Ada\",\"extra\":true}", false)]
    [InlineData("{\"name\":\"Ada\",\"age\":2.5}", false)]
    [InlineData("not json", false)]
    [InlineData("{\"name\":\"Ada\",\"name\":\"Grace\"}", false)]
    public void ValidatesObjectPayloads(string payload, bool valid) =>
        Assert.Equal(valid, BehaviorSchema.Validate(Person, payload).Count == 0);

    [Theory]
    [InlineData("[]", true)]
    [InlineData("[null,\"hello\"]", true)]
    [InlineData("[true]", false)]
    [InlineData("null", false)]
    public void ValidatesNullableArrayItems(string payload, bool valid) =>
        Assert.Equal(valid, BehaviorSchema.Validate("""{"type":"array","items":{"type":["string","null"]}}""", payload).Count == 0);

    [Theory]
    [InlineData("{\"minimum\":1}")]
    [InlineData("{\"type\":\"unknown\"}")]
    [InlineData("{\"type\":[]}")]
    [InlineData("{\"required\":[3]}")]
    [InlineData("{\"properties\":[]}")]
    [InlineData("{\"additionalProperties\":{\"type\":\"string\"}}")]
    [InlineData("{\"anyOf\":[]}")]
    [InlineData("{\"$ref\":\"https://example.org/schema\"}")]
    [InlineData("{\"$ref\":\"#/$defs/missing\"}")]
    [InlineData("{\"$ref\":\"#\",\"type\":\"string\"}")]
    public void RejectsUnsupportedOrMalformedSchemas(string schema)
    {
        Assert.NotEmpty(BehaviorSchema.CheckSchema(schema));
        Assert.NotEmpty(BehaviorSchema.Validate(schema, "{}"));
        Assert.False(BehaviorSchema.IsAssignable(schema, schema));
    }

    [Fact]
    public void ResolvesNestedLocalReferencesAndChecksReferencedValues()
    {
        const string schema = """{"$defs":{"label":{"type":"string","enum":["ready","done"]}},"type":"object","properties":{"state":{"$ref":"#/$defs/label"}},"required":["state"]}""";
        Assert.Empty(BehaviorSchema.CheckSchema(schema));
        Assert.Empty(BehaviorSchema.Validate(schema, """{"state":"ready"}"""));
        Assert.NotEmpty(BehaviorSchema.Validate(schema, """{"state":"invalid"}"""));
    }

    [Fact]
    public void ChecksAnyOfAndSiblingConstraints()
    {
        const string schema = """{"type":"string","anyOf":[{"const":"ready"},{"const":1}]}""";
        Assert.Empty(BehaviorSchema.Validate(schema, "\"ready\""));
        Assert.NotEmpty(BehaviorSchema.Validate(schema, "1"));
        Assert.NotEmpty(BehaviorSchema.Validate(schema, "\"done\""));
    }

    [Theory]
    [InlineData("{\"type\":\"integer\"}", "{\"type\":\"number\"}", true)]
    [InlineData("{\"type\":\"number\"}", "{\"type\":\"integer\"}", false)]
    [InlineData("{\"type\":\"string\"}", "{\"type\":\"boolean\"}", false)]
    [InlineData("{\"type\":\"string\"}", "{\"type\":[\"string\",\"null\"]}", true)]
    [InlineData("{\"type\":[\"string\",\"null\"]}", "{\"type\":\"string\"}", false)]
    [InlineData("{\"const\":\"ready\"}", "{\"enum\":[\"ready\",\"done\"]}", true)]
    [InlineData("{\"enum\":[\"ready\",\"bad\"]}", "{\"enum\":[\"ready\",\"done\"]}", false)]
    [InlineData("{\"type\":\"array\",\"items\":{\"type\":\"string\"}}", "{\"type\":\"array\",\"items\":{\"type\":\"number\"}}", false)]
    [InlineData("true", "{}", true)]
    [InlineData("true", "{\"type\":\"string\"}", false)]
    public void ProvesConservativeAssignability(string output, string input, bool assignable) =>
        Assert.Equal(assignable, BehaviorSchema.IsAssignable(output, input));

    [Fact]
    public void AssignabilityRequiresFieldsAndRejectsUnconstrainedExtras()
    {
        Assert.True(BehaviorSchema.IsAssignable(Person, Person));
        Assert.False(BehaviorSchema.IsAssignable("""{"type":"object","properties":{"name":{"type":"string"}}}""", Person));
        Assert.False(BehaviorSchema.IsAssignable("""{"type":"object","required":["name"]}""", Person));
        Assert.True(BehaviorSchema.IsAssignable(Person, """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}"""));
    }

    [Fact]
    public void IdenticalReferenceTextDoesNotHideDifferentDefinitionRoots()
    {
        const string output = """{"$defs":{"value":{"type":"string"}},"type":"object","properties":{"value":{"$ref":"#/$defs/value"}}}""";
        const string input = """{"$defs":{"value":{"type":"number"}},"type":"object","properties":{"value":{"$ref":"#/$defs/value"}}}""";
        Assert.False(BehaviorSchema.IsAssignable(output, input));
    }

    [Fact]
    public void RecursiveReferencesAreBoundedAndCanValidateFiniteTrees()
    {
        const string tree = """{"type":"object","properties":{"child":{"anyOf":[{"type":"null"},{"$ref":"#"}]}},"additionalProperties":false}""";
        Assert.Empty(BehaviorSchema.CheckSchema(tree));
        Assert.Empty(BehaviorSchema.Validate(tree, """{"child":{"child":null}}"""));
        Assert.NotEmpty(BehaviorSchema.Validate("""{"$ref":"#"}""", "{}"));
        Assert.False(BehaviorSchema.IsAssignable("""{"$ref":"#"}""", """{"type":"string"}"""));
    }

    [Theory]
    [InlineData("1.0", true)]
    [InlineData("1e10000", true)]
    [InlineData("0e-10000", true)]
    [InlineData("1e-10000", false)]
    [InlineData("100e-2", true)]
    [InlineData("100e-3", false)]
    [InlineData("1.000000000000000000000000000001", false)]
    public void IntegerValidationPreservesNumericPrecision(string payload, bool valid) =>
        Assert.Equal(valid, BehaviorSchema.Validate("""{"type":"integer"}""", payload).Count == 0);

    [Fact]
    public void NestedObjectCompatibilityChecksOptionalValuesToo()
    {
        const string output = """{"type":"object","properties":{"nested":{"type":"object","properties":{"value":{"type":"string"}},"required":["value"]}},"required":["nested"],"additionalProperties":false}""";
        const string input = """{"type":"object","properties":{"nested":{"type":"object","properties":{"value":{"type":"number"}},"required":["value"]}},"required":["nested"]}""";
        Assert.False(BehaviorSchema.IsAssignable(output, input));
        Assert.NotEmpty(BehaviorSchema.Validate(output, """{"nested":{"value":1}}"""));
        Assert.Empty(BehaviorSchema.Validate(output, """{"nested":{"value":"ok"}}"""));
    }
    [Fact]
    public void ResourceLimitsRejectOversizedAndDeepInputs()
    {
        Assert.NotEmpty(BehaviorSchema.CheckSchema(new string(' ', 1_048_577)));
        Assert.NotEmpty(BehaviorSchema.Validate("{}", new string('[', 65) + "0" + new string(']', 65)));
    }
}

