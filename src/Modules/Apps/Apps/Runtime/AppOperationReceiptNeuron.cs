using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Apps;

[GenerateSerializer, Alias("apps.operation-receipt-state")]
internal sealed class AppOperationReceiptState
{
    [Id(0)] public AppDispatchReceipt? Receipt { get; set; }
}

[Alias("apps.operation-receipt"), DefaultGrainType("apps.operation-receipt")]
internal interface IAppOperationReceipt : IGrainWithStringKey
{
    Task<AppDispatchReceipt?> Read();
    Task Store(AppDispatchReceipt receipt);
}

/// <summary>One durable retry result per operation, so the app root does not grow with run history.</summary>
[GrainType("apps.operation-receipt")]
internal sealed class AppOperationReceiptNeuron(
    [PersistentState("receipt", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<AppOperationReceiptState> state) : Grain, IAppOperationReceipt
{
    public Task<AppDispatchReceipt?> Read() => Task.FromResult(state.State.Receipt);
    public async Task Store(AppDispatchReceipt receipt)
    {
        if (state.State.Receipt is { } existing)
        {
            if (existing.Request != receipt.Request) { throw new InvalidOperationException("Conflicting operation receipt."); }
            return;
        }
        state.State = new() { Receipt = receipt };
        try { await state.WriteStateAsync(); }
        catch { state.State = new(); throw; }
    }
}
