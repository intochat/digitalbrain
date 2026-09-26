# Neuron registry

Selected modules may implement `INeuronRegistryContributor` to declare stable neuron contract IDs. Composition validates an immutable local snapshot; `NeuronRegistrationStartupTask` publishes its serializable records to the persistent `INeuronRegistryGrain` before the silo finishes starting. Discovery reads that grain. There is no lazy DI factory or assembly scan.

The registry lists contracts, not grain instances. `AgentRoutable` controls Discovery indexing, not execution permission. A registry read before startup registration completes fails instead of returning a misleading empty inventory. The snapshot's content hash is the grain key: silos with the same selected modules publish idempotently to one grain, while different module sets use separate grains. Discovery reads the version selected by its local silo, so an older silo cannot overwrite a newer snapshot during a rolling deployment.

Current contributions: `TimeModule` declares `time.timer` (`ITimer`) and `time.reminder` (`IReminder`). Other modules can opt in incrementally; the current inventory is not a complete list of every neuron in the repository.
