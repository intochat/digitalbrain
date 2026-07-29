using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using Xunit;

namespace DigitalBrain.Behaviors.Tests;

public sealed class CompilerSmoke
{
    [Fact(DisplayName = "compiler smoke")]
    public void CompilesGreenProgram()
    {
        var compiler = new ContractOnlyBehaviorCompiler();
        var result = compiler.Compile(RailPrograms.GreenProgram(), new BehaviorId("com.digitalbrain.sample"));
        Assert.True(result.Succeeded, result.Diagnostics);
    }
}
