using DigitalBrain.Execution;

namespace DigitalBrain.Modules.Execution.Contracts.Tests;

// Contract smoke: attempt goal/result vocabulary stays distinct wire shapes.
public sealed class GoalResultIdentityTests
{
    [Fact]
    public void Result_and_Failure_are_distinct_types()
    {
        Result? result = null;
        Failure? failure = null;
        Assert.Null(result);
        Assert.Null(failure);
        Assert.NotEqual(typeof(Result), typeof(Failure));
        Assert.True(typeof(Result).IsAbstract || typeof(Result).IsClass);
    }

    [Fact]
    public void ExecutionPolicy_holds_attempt_bounds()
    {
        var policy = new ExecutionPolicy(
            MaximumAttempts: 1,
            RetryDelay: TimeSpan.FromSeconds(1),
            Deadline: null);
        Assert.Equal(1, policy.MaximumAttempts);
        Assert.Equal(TimeSpan.FromSeconds(1), policy.RetryDelay);
    }
}
