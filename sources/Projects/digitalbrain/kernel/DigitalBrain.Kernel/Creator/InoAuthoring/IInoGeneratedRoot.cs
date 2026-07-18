namespace DigitalBrain.Kernel.Creator.InoAuthoring;

// E-SDK #57 sub-issue B. Abstracts the filesystem root that holds
// Creator-authored `.ino` documents. Production default points at
// `src/domains/dynamic/.../Generated/`; tests inject a temp directory
// so they don't pollute the source tree. The path is RESOLVED at
// service construction — silo cold-start handlers (InoNeuronStore on
// promote, DynamicGeneratedInoSource on DiscoverAsync) read from this
// one root rather than re-deriving it from environment variables.
public interface IInoGeneratedRoot
{
    string AbsolutePath { get; }
}
