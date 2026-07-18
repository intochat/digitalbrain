using Ino.Core;
using Xunit;

namespace Ino.Core.Tests;

public class SynapseErrorCodeTests
{
    [Fact]
    public void SynapseError_carries_typed_code()
    {
        var err = new SynapseError(SynapseErrorCode.NoCanonicalHandler, "nope");
        Assert.Equal(SynapseErrorCode.NoCanonicalHandler, err.Code);
    }

    [Fact]
    public void NeuronResult_Fail_accepts_typed_error()
    {
        var err = new SynapseError(SynapseErrorCode.CapabilityDenied, "denied");
        var result = NeuronResult.Fail(err);
        Assert.False(result.Success);
        Assert.Equal(SynapseErrorCode.CapabilityDenied, result.Error!.Code);
    }
}
