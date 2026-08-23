using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Execution;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class TestEchoCapabilityHandler : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("test.echo");

    public Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        string requestJson,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContextDelta(
            new ContextPath("test.echo"),
            SchemaHash: "test.echo.v1",
            PayloadJson: """{"result":"pong"}""",
            BlobRef: null));
    }
}
