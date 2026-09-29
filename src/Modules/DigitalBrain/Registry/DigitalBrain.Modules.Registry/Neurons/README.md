# Neuron registry

`RegistryModule` registers `NeuronDiscoveryStartupTask`, which reads Kernel's selected `ModuleInventory` through `NeuronRegistry` and scans each selected module's contract assembly for public interfaces extending `INeuron`. Every such interface needs a unique Orleans `[Alias]`; that alias is its discovery ID. The resulting registry lives in the silo's service provider. The capability catalog and neuron agent tools read it directly.

The registry lists contracts, not grain instances. It does not register implementations, grant access, persist state, or coordinate silos. The interface name and method names provide searchable text without a second metadata declaration.
