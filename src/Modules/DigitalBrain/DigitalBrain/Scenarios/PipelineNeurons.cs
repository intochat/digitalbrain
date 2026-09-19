using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

[GrainType("download")]
public sealed class DownloadNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type == "Tick"
            ? SendSignal(Signal.Create("Downloaded", """{"zip":"rates.zip","bytes":2048}"""), delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}

[GrainType("archive")]
public sealed class ArchiveNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type == "Downloaded"
            ? SendSignal(Signal.Create("CsvReady", """{"rows":[{"ccy":"EUR","rate":1.08},{"ccy":"GBP","rate":1.27}]}"""), delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}

[GrainType("clickhouse")]
public sealed class ClickHouseNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type == "CsvReady"
            ? SendSignal(Signal.Create("Ingested", """{"table":"fx","rows":2}"""), delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}

[GrainType("mail")]
public sealed class MailNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
        => delivery.Signal.Type is "CsvReady" or "EmailArrived"
            ? SendSignal(Signal.Create("Mailed", delivery.Signal.Body), delivery.CorrelationId, cancellationToken)
            : Task.CompletedTask;
}
