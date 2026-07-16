namespace Brain.Contracts;

[Alias("brain.neuron.v2")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("describe")] Task<NeuronDescription> DescribeAsync();
    [Alias("read")] Task<NeuronSnapshot> ReadAsync(string projection);
    [Alias("invoke")] Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);
    [Alias("events")] Task<NeuronEventPage> ReadEventsAsync(long fromRevision, int max);
}
