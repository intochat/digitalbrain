using DigitalBrain.Core;
using DigitalBrain.Kernel.Foundry;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Foundry;

public class CodeRunNeuronWiringTests : NeuronTestBase
{
    [Fact]
    public async Task CodeRunNeuron_Executes_Generated_Code()
    {
        var neuron = Grain<ICodeRunNeuron>("foundry-coderun-smoke");
        const string source = """
            public static class Program
            {
                public static string Run(System.Collections.Generic.Dictionary<string, object?> args) => "ok";
            }
            """;

        await neuron.FireAsync(new RunGeneratedCode(source, "Run"));

        var outgoing = await neuron.GetOutgoingTimelineAsync();
        var result = Assert.Single(outgoing.OfType<CodeRunResult>());
        Assert.True(result.Success, result.Error);
        Assert.Equal("ok", result.Output);
    }
}
