neuron DigitalBrain.Genesis
  "Spawns the Orleans cluster, ensures a Primary brain exists."

  using loaded   = synapse(DigitalBrain.Kernel.Loaded)
  using brains   = neuron(DigitalBrain.BrainRegistry)
  using created  = synapse(DigitalBrain.BrainCreated)

  @telemetry:counter:genesis_runs

  on loaded:
    log "genesis: ensuring primary brain exists"
    count genesis_runs
    let existing = ask brains to "list"
    if existing:
      log "genesis: brains exist already"
    else:
      ask brains to "create primary"
      emit created(brainId: "primary")

scenario "genesis ensures primary brain is created when list is empty"
  given brains returns ""
  when  synapse loaded()
  then  synapse created emitted with brainId == "primary"
  and   counter genesis_runs == 1
