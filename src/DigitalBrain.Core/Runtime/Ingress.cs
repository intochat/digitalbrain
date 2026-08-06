namespace DigitalBrain;

[Alias("db.ingress")]
internal interface IIngress : IGrainWithStringKey
{
    [Alias("emit")]
    Task EmitAsync(Synapse fact);
}

[GrainType(GrainTypeName)]
internal sealed class Ingress : Neuron, IIngress
{
    internal const string GrainTypeName = "digitalbrain.ingress";

    internal static NeuronId IdFor(string context) => new(NeuronId.KindOf(typeof(Ingress)), context);

    Task IIngress.EmitAsync(Synapse fact) => EmitIngressAsync(fact);
}
