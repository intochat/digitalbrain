namespace DigitalBrain.Tests.Unit.Policies;

public sealed class PolicyGrammarFacts
{
    private static readonly ActionDescriptor Charges = new("charges.charge", "charges", "USD");
    private static readonly ActionDescriptor Drafts = new("gmail.draft.create", "create a mail draft", null);

    [Fact]
    public void AWeeklySumOfThreeHundredRendersTheSentence()
    {
        var rule = PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Week", 300m, null, null, null, Charges, TimeProvider.System);

        Assert.Equal("Allow at most 300 USD of charges per week", rule.Text);
        Assert.Equal("Allow at most 300 USD of charges per week", PolicyGrammar.Render(rule, Charges));
    }

    [Fact]
    public void SumOnAnActionWithoutAnAmountUnitIsAGrammarError()
    {
        var error = Assert.Throws<PolicyGrammarException>(
            () => PolicyGrammar.Parse("r1", "gmail.draft.create", "Sum", "Day", 5m, null, null, null, Drafts, TimeProvider.System));

        Assert.Contains("grammar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARollingWindowUnderOneHourIsAGrammarError()
    {
        var error = Assert.Throws<PolicyGrammarException>(
            () => PolicyGrammar.Parse("r1", "charges.charge", "Sum", "Rolling:00:30:00", 100m, null, null, null, Charges, TimeProvider.System));

        Assert.Contains("grammar", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnUnknownOpIsAGrammarError()
    {
        var error = Assert.Throws<PolicyGrammarException>(
            () => PolicyGrammar.Parse("r1", "gmail.draft.create", "Count", "Day", 5m, "To", "MatchesRegex", "@corp.com", Drafts, TimeProvider.System));

        Assert.Contains("grammar", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
