namespace DigitalBrain.Runtime.Dynamic;

public static class DynamicSynapseTypes
{
    public static readonly string CreateNeuronRequest     = typeof(CreateNeuronRequest).FullName!;
    public static readonly string NeuronCreated           = typeof(NeuronCreated).FullName!;
    public static readonly string PlanNeuronRequest       = typeof(PlanNeuronRequest).FullName!;
    public static readonly string PlanNeuronResponse      = typeof(PlanNeuronResponse).FullName!;
    public static readonly string CreatorProgress         = typeof(CreatorProgress).FullName!;
    public static readonly string AuthorInoNeuronRequest  = typeof(AuthorInoNeuronRequest).FullName!;
    public static readonly string UiLayoutTransitionRequested = typeof(UiLayoutTransitionRequested).FullName!;
    public static readonly string LayoutStateTransition = typeof(LayoutStateTransition).FullName!;
}

