neuron DigitalBrain.WidgetCanvas.FlightNeuron
  "Spins up a 3D globe panel with an animated route arc on 'show flight <code>'."

  using showFlight = synapse(DigitalBrain.WidgetCanvas.ShowFlight)
  using shown      = synapse(DigitalBrain.WidgetCanvas.FlightShown)

  on showFlight:
    log "flight: showing globe for {showFlight.code}"
    emit shown(code: showFlight.code)

  ui:
    EarthGlobe(
      autoRotate: true,
      points: [{lat: 51.47, lng: -0.45}, {lat: 40.64, lng: -73.78}],
      arcs: [{from: {lat: 51.47, lng: -0.45}, to: {lat: 40.64, lng: -73.78}, style: "dashed"}]
    )

scenario "show flight surfaces the globe"
  when synapse showFlight(code: "BA286")
  then synapse shown emitted with code == "BA286"
