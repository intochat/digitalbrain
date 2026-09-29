# Registry module consolidation

The owner approved consolidating Discovery and Kernel's neuron registry on 2026-09-29.

Rename `Modules/DigitalBrain/Discovery` and its projects, namespaces, configuration types, and module to Registry. Registry owns neuron contract discovery, method metadata, JSON invocation, capability search, and unmet intents. Move the neuron agent-tool adapter from AI into Registry; it consumes AI contracts without introducing an AI implementation dependency.

Kernel retains runtime and composition. Expose selected module types through an immutable `ModuleInventory` registered by both production hosting and the unit harness. Registry consumes that inventory and registers its own startup task. A host without Registry must have no registry services or discovery startup task. Do not scan all loaded assemblies.

Preserve Orleans aliases, grain types, storage names, method IDs, the existing `/discovery/unmet-intents` route, and capability search behavior. Module configuration identities and package/namespace names change to Registry. Update current consumers and deployment fixtures together. Historical design documents remain historical and link to this replacement where appropriate.

Move registry/invoker tests to Registry. Keep generic AI turn behavior tests in AI and move only neuron schema tests. Move the Time discovery integration test to Registry to avoid coupling Time's test suite to search infrastructure.

Verification covers optional registry selection, selected-only discovery, callable neuron tools, JSON invocation, search and workspace isolation, Kernel runtime, AI turns, Assistant integration, deployment fixtures, and solution compilation.
