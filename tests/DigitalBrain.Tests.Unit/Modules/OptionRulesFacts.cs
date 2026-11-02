using DigitalBrain.Modules.Validation;

namespace DigitalBrain.Tests.Unit.Modules;

public sealed class OptionRulesFacts
{
    [Fact]
    public void RequiredIsBrokenByNullAndBlank()
    {
        Assert.True(new Required("Key", null).Broken);
        Assert.True(new Required("Key", "  ").Broken);
        Assert.Equal("A value is required.", new Required("Key", null).Message);
    }

    [Fact]
    public void RequiredHoldsForAValue()
    {
        Assert.False(new Required("Key", "x").Broken);
        Assert.False(new Required("Key", 0).Broken);
    }

    [Fact]
    public void StringLengthCountsScalarsAndIgnoresNull()
    {
        Assert.False(new StringLength("Key", "\U0001F600\U0001F600", 1, 2).Broken);
        Assert.True(new StringLength("Key", "\U0001F600\U0001F600\U0001F600", 1, 2).Broken);
        Assert.True(new StringLength("Key", "", 1, 2).Broken);
        Assert.False(new StringLength("Key", null, 1, 2).Broken);
        Assert.Equal("Use between 1 and 2 characters.", new StringLength("Key", "", 1, 2).Message);
    }

    [Fact]
    public void Utf8LengthCountsBytes()
    {
        Assert.False(new Utf8Length("Key", "ab", 2).Broken);
        Assert.True(new Utf8Length("Key", "é", 1).Broken);
        Assert.False(new Utf8Length("Key", null, 1).Broken);
        Assert.Equal("Use at most 1 UTF-8 bytes.", new Utf8Length("Key", "é", 1).Message);
    }

    [Fact]
    public void ArrayLengthBoundsItems()
    {
        Assert.False(new ArrayLength<int>("Key", [1, 2], 1, 2).Broken);
        Assert.True(new ArrayLength<int>("Key", [1, 2, 3], 1, 2).Broken);
        Assert.True(new ArrayLength<int>("Key", [], 1, 2).Broken);
        Assert.False(new ArrayLength<int>("Key", null, 1, 2).Broken);
        Assert.Equal("Use between 1 and 2 items.", new ArrayLength<int>("Key", [], 1, 2).Message);
    }

    [Fact]
    public void RangeBoundsValue()
    {
        Assert.False(new NumberRange("Key", 5m, 1m, 5m).Broken);
        Assert.True(new NumberRange("Key", 6m, 1m, 5m).Broken);
        Assert.True(new NumberRange("Key", 0m, 1m, 5m).Broken);
        Assert.Equal("Use a value between 1 and 5.", new NumberRange("Key", 0m, 1m, 5m).Message);
    }

    [Fact]
    public void HttpUriWantsAbsoluteHttpOrHttps()
    {
        Assert.False(new HttpUri("Key", new Uri("https://example.test/")).Broken);
        Assert.False(new HttpUri("Key", null).Broken);
        Assert.True(new HttpUri("Key", new Uri("ftp://example.test/")).Broken);
        Assert.True(new HttpUri("Key", new Uri("/relative", UriKind.Relative)).Broken);
        Assert.Equal("Use an absolute HTTP or HTTPS URI.", new HttpUri("Key", null).Message);
    }

    [Fact]
    public void BoundsAreProgrammerErrors()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StringLength("Key", "a", 3, 2).Broken);
        Assert.Throws<ArgumentOutOfRangeException>(() => new NumberRange("Key", 1m, 3m, 2m).Broken);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Utf8Length("Key", "a", -1).Broken);
    }

    [Fact]
    public void ModuleValidationListsEveryBrokenOption()
    {
        var ex = Assert.Throws<ModuleValidationException>(() => ModuleValidation.Check(
            "mail",
            new Required("Host", null),
            new NumberRange("Port", 70000m, 1m, 65535m),
            new HttpUri("Endpoint", new Uri("https://ok.test/")),
            new StringLength("Name", "", 1, 8)));

        Assert.Equal(
            [
                new ValidationIssue("Host", "required", "A value is required."),
                new ValidationIssue("Port", "range", "Use a value between 1 and 65535."),
                new ValidationIssue("Name", "length", "Use between 1 and 8 characters."),
            ],
            ex.Issues);
    }

    [Fact]
    public void ModuleValidationPassesWhenNothingIsBroken() =>
        ModuleValidation.Check("mail", new Required("Host", "smtp"), new NumberRange("Port", 25m, 1m, 65535m));
}
