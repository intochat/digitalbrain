namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. The persistence neuron — abstracts the on-disk
// Generated/<neuron-id>/ layout away from InoAuthoringLoop so unit tests
// don't touch the filesystem and integration tests can point at a
// temp directory. The store decides slug→path mapping; the loop just
// hands it `(fqn, intent, llmModel, source)` and gets back the relative
// `.ino` path used to feed `InoScenarioProjection.RunAsync` for the
// verification gate.
public interface IInoNeuronStore
{
    // Returns the .ino file's path relative to the Generated root. The
    // root itself is held by the IInoGeneratedRoot abstraction the store
    // depends on, so callers can pass that root to
    // `InoScenarioProjection.RunAsync` for the L6 gate.
    Task<string> SaveAsync(
        InoNeuronManifest manifest,
        string inoSource,
        CancellationToken cancellationToken);
}
