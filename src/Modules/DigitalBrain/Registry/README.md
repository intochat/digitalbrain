# Registry

Registry owns the inventory of callable neuron contracts and capability search. Select `RegistryModule` alongside `QdrantModule` to enable it. Other modules publish searchable capabilities through `ICapabilitySource`; AI consumes the tool factories through its own contracts.

Kernel hosting and the unit harness publish the selected module types as `ModuleInventory`. Registry registers `NeuronDiscoveryStartupTask`, which scans only those modules' contract assemblies. The local `NeuronRegistry` lists contracts and callable methods; `NeuronInvoker` binds JSON arguments and calls them through the grain factory.

The capability catalog combines neuron methods and contributed sources, with keyword search and optional embedding/vector search. Workspace visibility, invalidation, degraded search, and unmet-intent recording remain part of the catalog. Registry also supplies `NeuronTools` and `CapabilityTools` to agents, so search results can offer the corresponding callable tools.

Hosts without Registry keep normal typed neuron calls and subscriptions, with no registry services or scanning. AI does not require Registry to run turns with other tool providers.

The former Discovery packages and namespaces are now `DigitalBrain.Modules.Registry*` and `DigitalBrain.Registry*`. Replace `DiscoveryModule` with `RegistryModule` in compositions and module configuration. Existing Orleans aliases (including `discovery.*`), grain types, and the `/discovery/unmet-intents` endpoint remain unchanged for compatibility.
