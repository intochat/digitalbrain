namespace DigitalBrain.Runtime;

// Shell grain that hosts a Roslyn-scripted neuron. The cluster-wide
// INeuronRegistry stores the spec; activating a DynamicNeuronGrain by NeuronId
// loads the script. Invoke routes a typed synapse through the script as
// (payloadJson, typeName) — JSON-as-bytes plus the originating type FQN — to
// match the gateway's existing wire format. Returns the response payload as JSON.
[Orleans.Metadata.DefaultGrainType("DynamicNeuronGrain")]
public interface IDynamicNeuron : IGrainWithStringKey
{
    Task LoadAsync(DynamicNeuronSpec spec);
    Task<DynamicNeuronSpec?> GetSpecAsync();
    Task<string> InvokeAsync(string payloadJson, string typeName, CorrelationId correlationId);
}
