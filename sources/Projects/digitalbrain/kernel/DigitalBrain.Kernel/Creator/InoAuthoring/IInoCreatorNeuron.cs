using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 close-out. Marker interface for the InoLang-retargeted Creator
// grain — the synapse-on-grain entry point that delegates `AuthorInoNeuronRequest`
// to InoAuthoringLoop and broadcasts `DigitalBrain.Creator.InoNeuronAuthored`
// on outcome. Parallel to ICreator (the legacy C#-triplet path), kept
// separate so the InoLang surface can be retired or replaced independently
// once E-INO closes the substrate phase.
public interface IInoCreatorNeuron : INeuron;
