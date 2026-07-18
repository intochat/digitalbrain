neuron DigitalBrain.System
  "The distributed OS coordinator. Starts core services, manages dynamic resources, and binds the visual shell."

  using loaded            = synapse(DigitalBrain.Kernel.Loaded)
  using brains            = neuron(DigitalBrain.BrainRegistry)
  using aspire            = neuron(DigitalBrain.SDK.AspireRuntime)
  using telemetry         = neuron(DigitalBrain.SDK.TelemetryTracker)
  using created           = synapse(DigitalBrain.BrainCreated)
  using resourceAdded     = synapse(DigitalBrain.ResourceAdded)

  @telemetry:counter:system_boots
  @telemetry:counter:resources_registered

  on loaded:
    log "system: initializing DigitalBrain substrate"
    count system_boots

    # Ensure the primary Orleans brain context exists
    let existing = ask brains to "list"
    if existing:
      log "system: existing brains discovered in Orleans storage"
    else:
      log "system: genesis flow - creating primary brain container"
      ask brains to "create primary"
      emit created(brainId: "primary")

    # Dynamically compose and register distributed Aspire resources
    log "system: mapping distributed application topography via Aspire API"
    
    # Core database clustering
    ask aspire to "register-resource orleans-redis type:container port:59330"
    count resources_registered
    emit resourceAdded(name: "orleans-redis", type: "container")

    # Personal assistant visual environments
    ask aspire to "register-resource flutter-web type:executable path:../../UI/flutter args:run -d web-server --release --wasm port:5800"
    count resources_registered
    emit resourceAdded(name: "flutter-web", type: "executable")

    ask aspire to "register-resource flutter-windows type:executable path:../../UI/flutter args:run -d windows --print-dtd port:5821 autostart:false"
    count resources_registered
    emit resourceAdded(name: "flutter-windows", type: "executable")

    # Code intelligence & developer sidecars
    ask aspire to "register-resource digitalbrain-mcp type:project path:sdk/DigitalBrain.SDK.Mcp port:5810"
    count resources_registered
    emit resourceAdded(name: "digitalbrain-mcp", type: "project")

    log "system: distributed application topography successfully established. RFW layers active."

scenario "system boots and registers core distributed topography"
  given brains returns ""
  when  synapse loaded()
  then  synapse created emitted with brainId == "primary"
  and   synapse resourceAdded emitted with name == "orleans-redis"
  and   counter system_boots == 1
