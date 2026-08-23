using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Execution;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class TestEchoCapabilityHandler : ICapabilityHandler
{
    public CapabilityId Id { get; } = CapabilityId.Parse("test.echo");

    public Task<ContextDelta> InvokeAsync(
        ExecutionId executionId,
        OwnerId owner,
        string requestJson,
        CancellationToken cancellationToken)
    {
        _ = executionId;
        _ = owner;
        _ = requestJson;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ContextDelta(
            new ContextPath("test.echo"),
            SchemaHash: "test.echo.v1",
            PayloadJson: """{"result":"pong"}""",
            BlobRef: null));
    }
}
