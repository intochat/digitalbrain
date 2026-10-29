using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

public sealed class ConfirmationGateFacts
{
    [Fact]
    public async Task AStorageWriteIsRefusedWhenTheHostHasNoConfirmationService()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var request = new OperationRequest(new("op-test"), new("owner"), "owner/files/main", WritePlan.Delete(new("file")));

        var decision = await new ConfirmingOperationGate(services).Check(request, TestContext.Current.CancellationToken);

        Assert.False(decision.Allowed);
        Assert.Contains("confirmation", decision.Reason!, StringComparison.OrdinalIgnoreCase);
    }
}
