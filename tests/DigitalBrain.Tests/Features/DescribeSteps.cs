using System.Text.Json;
using System.Text.Json.Nodes;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Core;
using DigitalBrain.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests;

[Binding]
public sealed class DescribeSteps(BrainSteps brain)
{
    private BrainOperations? _operations;
    private IReadOnlyList<MethodDescriptor>? _description;
    private JsonElement? _callResult;
    private Exception? _lastError;
    private JournalRead? _read;

    [When(@"""(.*)"" describes counter ""(.*)""")]
    public async Task Describe(string _, string name)
        => _description = await Operations().DescribeAsync(new DescribeRequest(Neuron: $"counter:{name}"));

    [Then(@"the description lists interface ""(.*)"" with methods ""(.*)"" and ""(.*)""")]
    public void ThenInterfaceMethods(string interfaceAlias, string first, string second)
    {
        Assert.NotNull(_description);
        var methods = _description.Where(method => method.InterfaceAlias == interfaceAlias).Select(method => method.MethodAlias);
        Assert.Contains(first, methods);
        Assert.Contains(second, methods);
    }

    [Then(@"method ""(.*)"" args schema requires ""(.*)"" and ""(.*)""")]
    public void ThenRequiredArgs(string methodAlias, string first, string second)
    {
        var required = Method(methodAlias).ArgsSchema!.Value.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        Assert.Contains(first, required);
        Assert.Contains(second, required);
    }

    [Then(@"method ""(.*)"" is read-only")]
    public void ThenReadOnly(string methodAlias) => Assert.True(Method(methodAlias).IsReadOnly);

    [When(@"""(.*)"" calls ""(.*)"" ""(.*)"" on counter ""(.*)"" with (\{.*\})$")]
    public Task CallCounter(string principal, string interfaceAlias, string methodAlias, string name, string json)
        => Call(principal, $"counter:{name}", interfaceAlias, methodAlias, json);

    [When(@"""(.*)"" calls ""(.*)"" ""(.*)"" on plain ""(.*)"" with (\{.*\})$")]
    public Task CallPlain(string principal, string interfaceAlias, string methodAlias, string name, string json)
        => Call(principal, name, interfaceAlias, methodAlias, json);

    [Then("the call returns a receipt and a work id")]
    public void ThenReceiptAndWork()
    {
        Assert.Null(_lastError);
        Assert.NotNull(_callResult);
        Assert.NotEqual(JsonValueKind.Null, _callResult.Value.GetProperty("receipt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, _callResult.Value.GetProperty("work").ValueKind);
    }

    [Then(@"the call fails naming ""(.*)""")]
    public void ThenFailsNaming(string interfaceAlias)
    {
        var error = Assert.IsType<ArgumentException>(_lastError);
        Assert.Contains(interfaceAlias, error.Message, StringComparison.Ordinal);
    }

    [Then("the call fails explaining there are no callable interfaces and naming the kernel tools")]
    public void ThenFailsWithKernelToolAdvice()
    {
        var error = Assert.IsType<ArgumentException>(_lastError);
        Assert.Contains("implements no callable interfaces", error.Message, StringComparison.Ordinal);
        Assert.Contains("kernel operations are the fire/connect/disconnect/read/cancel tools", error.Message, StringComparison.Ordinal);
    }

    [When(@"""(.*)"" fires (\d+) ""(\w+)"" signals at plain ""(.*)""")]
    public async Task FireSignals(string from, int count, string type, string to)
    {
        for (var i = 0; i < count; i++)
        {
            await brain.FireCore(from, type, "{}", to);
        }

        Assert.Null(brain.LastError);
    }

    [When(@"""(.*)"" reads ""(.*)"" incoming journal after sequence (\d+)")]
    public async Task ReadIncoming(string _, string name, long after)
        => _read = await brain.Neuron(name).ReadJournal(JournalKind.Incoming, after);

    [Then(@"the read reports a gap and an earliest retained sequence above (\d+)")]
    public void ThenGap(long sequence)
    {
        Assert.NotNull(_read);
        Assert.True(_read.Gap);
        Assert.True(_read.EarliestRetained > sequence);
    }

    private BrainOperations Operations()
        => _operations ??= new(brain.Brain.Grains, brain.Brain.SiloServices.GetRequiredService<INeuronInvoker>());

    private MethodDescriptor Method(string methodAlias)
    {
        Assert.NotNull(_description);
        return Assert.Single(_description, method => method.MethodAlias == methodAlias);
    }

    private async Task Call(string principal, string neuron, string interfaceAlias, string methodAlias, string json)
    {
        _lastError = null;
        _callResult = null;
        var arguments = JsonNode.Parse(json)!.AsObject();
        var handle = arguments["id"]!.GetValue<string>();
        arguments["id"] = new JsonObject { ["value"] = CommandSteps.CommandIdFrom(handle).Value.ToString() };
        var args = JsonSerializer.SerializeToElement(arguments);
        try
        {
            _callResult = await Operations().CallAsync(principal, new CallRequest(neuron, interfaceAlias, methodAlias, args));
        }
        catch (Exception error)
        {
            _lastError = error;
        }
    }
}
