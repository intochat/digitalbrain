# Neuron registry

Selected modules may implement `INeuronRegistryContributor` to declare stable neuron contract IDs. The registry lists contracts, not grain instances. `AgentRoutable` controls Discovery indexing, not execution permission. The Kernel registry does not scan assemblies or depend on Discovery.

Current contributions: `TimeModule` declares `time.timer` (`ITimer`) and `time.reminder` (`IReminder`). Other modules can opt in incrementally; the current inventory is not a complete list of every neuron in the repository.
