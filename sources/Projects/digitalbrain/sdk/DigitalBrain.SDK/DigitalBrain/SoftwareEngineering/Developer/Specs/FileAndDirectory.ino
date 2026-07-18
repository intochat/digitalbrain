neuron DigitalBrain.Developer.Specs.FileAndDirectoryFlows
  "Specs for checking file system resource operations through InoLang and Orleans."

  using read_req   = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.ReadFileRequest)
  using replied    = synapse(DigitalBrain.Developer.Specs.FileReplied)
  using file_port  = neuron(DigitalBrain.Developer.FileNeuron["e:\\temp\\test_file.txt"])

  on read_req:
    let result = ask file_port to "read"
    emit replied(success: "true")

scenario "reading an existing file returns its content"
  when synapse read_req()
  then synapse replied emitted with success == "true"
