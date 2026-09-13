using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionWorkspaceFacts
{
    [Fact]
    public async Task The_coding_module_composes_into_a_silo()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(CodingModule)]) });
        Assert.NotNull(brain.SiloServices.GetService(typeof(CodingModule)));
    }
}
