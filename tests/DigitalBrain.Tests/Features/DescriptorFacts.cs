using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DescriptorFacts
{
    [Fact]
    public void MemberTypesOutsideTheSpecAreRejected()
    {
        var options = DescriptorTable.ContractOptions(CounterJson.Default);
        var typeInfo = options.GetTypeInfo(typeof(BadArguments));
        var method = typeof(IBadCounter).GetMethod(nameof(IBadCounter.Bad))!;

        var error = Assert.Throws<InvalidOperationException>(() => DescriptorRules.ValidateMemberTypes(method, typeInfo));

        Assert.Contains(typeof(IBadCounter).FullName!, error.Message, StringComparison.Ordinal);
        Assert.Contains("method 'Bad'", error.Message, StringComparison.Ordinal);
        Assert.Contains("command.tags", error.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(Dictionary<string, string>).FullName!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DescribeCarriesTheXmlSummary()
    {
        await using var simulation = await BrainSteps.StartSimulationAsync();
        var invoker = simulation.SiloServices.GetRequiredService<INeuronInvoker>();

        Assert.Equal("Adds a count and schedules the increment.", invoker.Describe("test.counter", "add").Summary);
    }
}
