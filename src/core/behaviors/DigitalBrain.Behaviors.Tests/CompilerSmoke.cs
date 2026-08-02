using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;
using DigitalBrain.Behaviors.Runtime;
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
        Assert.Equal("contract-only-v1", result.Policy.PolicyId);
        Assert.Equal("Preview", result.Policy.LanguageVersion);
        Assert.Contains("\"policy\":\"contract-only-v1\"", result.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.Contains("\"languageVersion\":\"Preview\"", result.CompilerEvidenceJson, StringComparison.Ordinal);
        Assert.NotNull(result.Contract);
        Assert.NotEmpty(result.Contract!.Cases);
        Assert.Equal("case.SampleTrigger", result.Contract.Cases.Single().CaseId);
        Assert.DoesNotContain("\"oneOf\":[]", result.Contract.OneOfSchemaJson, StringComparison.Ordinal);
    }
}
