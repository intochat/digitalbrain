neuron DigitalBrain.UI.Specs.FlutterFlows
  "Specs for Remote Flutter Widgets UI layout composition and rendering."

  using rfw_card    = synapse(DigitalBrain.Runtime.Ui.RfwCard)
  using flutter     = neuron(DigitalBrain.UI.Flutter["canvas-ui"])

scenario "rendering dynamic RFW component card"
  given flutter returns "ok"
  when synapse rfw_card(LibraryName: "my_widgets", RootWidget: "MainView", DataJson: "{}")
