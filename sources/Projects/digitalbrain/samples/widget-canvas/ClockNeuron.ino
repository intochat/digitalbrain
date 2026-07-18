neuron DigitalBrain.WidgetCanvas.ClockNeuron
  "Shows an analog clock panel when the user says 'set a clock'."

  using setClock   = synapse(DigitalBrain.WidgetCanvas.SetClock)
  using clockShown = synapse(DigitalBrain.WidgetCanvas.ClockShown)

  on setClock:
    log "clock: showing analog clock ({setClock.timezone})"
    emit clockShown(timezone: setClock.timezone)

  ui:
    AnalogClock(showSeconds: true, face: "minimal")

scenario "set a clock surfaces the analog clock"
  when synapse setClock(timezone: "local")
  then synapse clockShown emitted with timezone == "local"
