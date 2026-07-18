namespace DigitalBrain.Runtime.Introspector;

public static class IntrospectorSynapseTypes
{
    public static readonly string FindNeuronsByFeatureTextRequest     = typeof(FindNeuronsByFeatureTextRequest).FullName!;
    public static readonly string FindChainsByConversationTextRequest = typeof(FindChainsByConversationTextRequest).FullName!;
    public static readonly string TraceCorrelationRequest             = typeof(TraceCorrelationRequest).FullName!;
    public static readonly string GetRecentActivityRequest            = typeof(GetRecentActivityRequest).FullName!;
    public static readonly string FindRootSynapseRequest              = typeof(FindRootSynapseRequest).FullName!;
    public static readonly string ExplainDecisionRequest              = typeof(ExplainDecisionRequest).FullName!;
    public static readonly string ExplainDecisionResponse             = typeof(ExplainDecisionResponse).FullName!;
    public static readonly string QueryCatalogContractsRequest        = typeof(QueryCatalogContractsRequest).FullName!;
    public static readonly string QueryCatalogContractsResponse       = typeof(QueryCatalogContractsResponse).FullName!;
}
