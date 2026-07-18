neuron DigitalBrain.Assistant.Core.Specs.PersonalAssistant
  "A premium personal assistant neuron that maps human productivity requests to local OS capabilities, like launching utilities or querying system state."

  using request   = synapse(DigitalBrain.Assistant.Core.Request)
  using win       = neuron(DigitalBrain.SDK.Windows.Runtime)
  using notepad   = neuron(DigitalBrain.SDK.Windows.Runtime["notepad"])
  using responded = synapse(DigitalBrain.Assistant.Core.Responded)

  on request where is-equal(request.Intent) is "open_notepad":
    let reply = ask notepad to "start"
    emit responded(Message: "I've launched Notepad for you. Output: {reply}")

  on request where is-equal(request.Intent) is "open_calculator":
    let reply = ask win to "start calc"
    emit responded(Message: "Sure, launching the calculator now. Output: {reply}")

  on request where is-equal(request.Intent) is "system_info":
    let reply = ask win to "system.info"
    emit responded(Message: "Here is your local system status: {reply}")

  on request where is-equal(request.Intent) is "run_powershell":
    let reply = ask win to "powershell Get-Date"
    emit responded(Message: "PowerShell output: {reply}")

  on request where is-equal(request.Intent) is "list_processes":
    let reply = ask win to "ps"
    emit responded(Message: "Processes: {reply}")

  on request where is-equal(request.Intent) is "kill_process":
    let reply = ask win to "kill notepad"
    emit responded(Message: "Kill status: {reply}")

  on request where is-equal(request.Intent) is "system_resources":
    let reply = ask win to "system.resources"
    emit responded(Message: "System resources: {reply}")

scenario "assistant launches notepad via a keyed process neuron"
  given is-equal(request.Intent) is "open_notepad"
  given notepad returns "Success: Process 'notepad.exe' launched with PID 1234."
  when synapse request(Intent: "open_notepad")
  then synapse responded emitted with Message == "I've launched Notepad for you. Output: Success: Process 'notepad.exe' launched with PID 1234."

scenario "assistant launches calculator via the unkeyed windows neuron"
  given is-equal(request.Intent) is "open_calculator"
  given win returns "Success: Process 'calc.exe' launched with PID 5678."
  when synapse request(Intent: "open_calculator")
  then synapse responded emitted with Message == "Sure, launching the calculator now. Output: Success: Process 'calc.exe' launched with PID 5678."

scenario "assistant queries local system information"
  given is-equal(request.Intent) is "system_info"
  given win returns "OS: Windows 11, Framework: .NET 11, Processors: 16, Machine: DESKTOP"
  when synapse request(Intent: "system_info")
  then synapse responded emitted with Message == "Here is your local system status: OS: Windows 11, Framework: .NET 11, Processors: 16, Machine: DESKTOP"

scenario "assistant executes powershell command"
  given is-equal(request.Intent) is "run_powershell"
  given win returns "Exit Code: 0\n\nSTDOUT:\nSunday, May 24, 2026\n\nSTDERR:\n"
  when synapse request(Intent: "run_powershell")
  then synapse responded emitted with Message == "PowerShell output: Exit Code: 0\n\nSTDOUT:\nSunday, May 24, 2026\n\nSTDERR:\n"

scenario "assistant lists running processes"
  given is-equal(request.Intent) is "list_processes"
  given win returns "Active Processes (Top 50):\nPID: 1001 - Name: dotnet"
  when synapse request(Intent: "list_processes")
  then synapse responded emitted with Message == "Processes: Active Processes (Top 50):\nPID: 1001 - Name: dotnet"

scenario "assistant terminates notepad process"
  given is-equal(request.Intent) is "kill_process"
  given win returns "Success: Terminated 1 instance(s) of 'notepad'."
  when synapse request(Intent: "kill_process")
  then synapse responded emitted with Message == "Kill status: Success: Terminated 1 instance(s) of 'notepad'."

scenario "assistant queries local system resources"
  given is-equal(request.Intent) is "system_resources"
  given win returns "Substrate Managed Memory: 15.20 MB\nHost Drives:\nDrive C:\\ - Free Space: 100.50 GB"
  when synapse request(Intent: "system_resources")
  then synapse responded emitted with Message == "System resources: Substrate Managed Memory: 15.20 MB\nHost Drives:\nDrive C:\\ - Free Space: 100.50 GB"
