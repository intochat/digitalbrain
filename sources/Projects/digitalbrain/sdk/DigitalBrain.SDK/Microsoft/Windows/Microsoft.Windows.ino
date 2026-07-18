neuron DigitalBrain.SDK.Windows.MicrosoftWindows
  "System-level OS manager neuron."
  using win = neuron(DigitalBrain.SDK.Windows.Runtime)
  on System.ExecuteCommand:
    let outcome = ask win to "run {it.appName} {it.args}"
    remember "lastExecutionStatus" outcome

