namespace DigitalBrain.Abstractions.Bundles;

// Source of locally or remotely available bundle install descriptors. The Kernel install path consumes
// this abstraction so a future Global-backed source can replace the local disk source without changing
// the installer contract.
public interface IBundleSource
{
    IReadOnlyList<IBundle> LoadBundles();
}
