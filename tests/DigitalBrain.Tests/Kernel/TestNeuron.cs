using Brain.Contracts;
using Brain.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Tests.Kernel;

public interface ITestNeuron : INeuron
{
    Task WriteStatusAsync(NeuronStatus status);
    Task<NeuronStatus> ReadStatusAsync();
    Task WriteOperationAsync(Guid operationId, ExternalOperation operation);
    Task<ExternalOperation?> ReadOperationAsync(Guid operationId);
    Task WriteNotificationAsync(Guid notificationId, NeuronNotification notification);
    Task<NeuronNotification?> ReadNotificationAsync(Guid notificationId);
}

public sealed class TestNeuron([NeuronState] NeuronDurableState state) : Neuron(state), ITestNeuron
{
    public async Task WriteStatusAsync(NeuronStatus status)
    {
        DurableState.Status.Value = status;
        await WriteStateAsync();
    }

    public Task<NeuronStatus> ReadStatusAsync() =>
        Task.FromResult(DurableState.Status.Value);

    public async Task WriteOperationAsync(Guid operationId, ExternalOperation operation)
    {
        DurableState.Operations[operationId] = operation;
        await WriteStateAsync();
    }

    public Task<ExternalOperation?> ReadOperationAsync(Guid operationId) =>
        Task.FromResult(
            DurableState.Operations.TryGetValue(operationId, out var operation)
                ? operation
                : null);

    public async Task WriteNotificationAsync(Guid notificationId, NeuronNotification notification)
    {
        DurableState.Outbox[notificationId] = notification;
        await WriteStateAsync();
    }

    public Task<NeuronNotification?> ReadNotificationAsync(Guid notificationId) =>
        Task.FromResult(
            DurableState.Outbox.TryGetValue(notificationId, out var notification)
                ? notification
                : null);
}
