using DigitalBrain.Rules;

namespace DigitalBrain.Tests.Unit;

public sealed class AppCheckFacts
{
    private sealed record Positive(int N) : Rule("positive")
    {
        public override bool Broken => N <= 0;

        public override string Message => $"{N} is not positive.";
    }

    [App("checked")]
    private sealed class Checked : App
    {
        public void Add(int n) => Check("add", new Positive(n));
    }

    [Fact]
    public void A_broken_rule_records_the_rejection_as_the_error_state_and_throws()
    {
        var app = new Checked();

        var error = Assert.Throws<RuleViolationException>(() => app.Add(-1));

        Assert.Equal(["positive"], error.Rejected.Rules);
        Assert.Equal(new AppError("add", nameof(Rejected), "-1 is not positive."), app.RecordedError);
    }

    [Fact]
    public void Rules_that_hold_record_nothing_and_do_not_throw()
    {
        var app = new Checked();

        app.Add(1);

        Assert.Null(app.RecordedError);
    }
}
