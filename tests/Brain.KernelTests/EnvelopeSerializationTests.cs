using Brain.Contracts;
using Xunit;

namespace Brain.KernelTests;

public class EnvelopeSerializationTests
{
    [Fact]
    public void Receipt_and_invocation_are_value_equal()
    {
        var a = new NeuronInvocation("chat.post.v1", "{}", "cmd-1", "session|dev|s/1");
        var b = a with { };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Brain_exception_carries_stable_code()
    {
        var exception = new BrainException(BrainErrors.GrantMissing, "no grant");
        Assert.Equal("grant.missing", exception.Code);
    }
}
