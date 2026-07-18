using Brain.Contracts;

namespace Brain.KernelTests;

public sealed class TestKind : INeuronKind
{
    public string Kind => "test";
    public string[] Contracts => ["test.echo.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "test.echo.v1" => ValueTask.FromResult(new KindResult(invocation.InputJson, [("echoed", invocation.InputJson)])),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        $$"""{"eventCount":{{context.Journal.Count}}}""";
}
