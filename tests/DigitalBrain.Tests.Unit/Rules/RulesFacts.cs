using System.Reflection;
using DigitalBrain.Rules;

namespace DigitalBrain.Tests.Unit.Rules;

internal sealed record NameRequired(string Value) : Rule("name.required")
{
    public override bool Broken => string.IsNullOrWhiteSpace(Value);

    public override string Message => "A name is required.";
}

internal sealed record NameShort(string Value) : Rule("name.short")
{
    public override bool Broken => Value.Length > 8;

    public override string Message => "A name has at most 8 characters.";
}

public sealed class RulesFacts
{
    [Fact]
    public void NoBrokenRuleIsNull() => Assert.Null(DigitalBrain.Rules.Rules.Judge("rename", new NameRequired("ok"), new NameShort("ok")));

    [Fact]
    public void EveryBrokenRuleIsListedInOrder()
    {
        var r = DigitalBrain.Rules.Rules.Judge("rename", new NameRequired(" "), new NameShort("123456789"))!;
        Assert.Equal("rename", r.Operation);
        Assert.Equal(["name.required", "name.short"], r.Rules);
        Assert.Equal(["A name is required.", "A name has at most 8 characters."], r.Messages);
    }

    [Fact]
    public void ExceptionCarriesTheRejection()
    {
        var r = DigitalBrain.Rules.Rules.Judge("rename", new NameRequired(""))!;
        var e = new RuleViolationException(r);
        Assert.Same(r, e.Rejected);
        Assert.Equal("A name is required.", e.Message);
    }

    [Fact]
    public void RejectedIsASignalWithAlias() => Assert.Equal("rule.rejected", typeof(Rejected).GetCustomAttribute<SignalAttribute>()!.Alias);
}
