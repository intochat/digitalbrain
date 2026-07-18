neuron DigitalBrain.Navigator.NavigatorNeuron
  "Pure declarative interpreted neuron coordinating developer sandboxing and folder creation routing."

  using prompt = synapse(DigitalBrain.Runtime.User.UserPromptReceived)
  using folder = synapse(DigitalBrain.Runtime.Introspector.CreateFolderRequest)
  using report = synapse(DigitalBrain.Runtime.Introspector.DeveloperSandboxReport)

  using store  = neuron(DigitalBrain.SDK.Developer.FileStore)

  on folder:
    let path = get-folder-path(folder.Prompt)
    let res = ask store to "create-folder {path}"
    let success = is-successful-spawn(res)
    emit report(Success: success, Message: "Folder operation completed", CreatedPath: path)

  on prompt where is-d-drive-prompt(prompt.Text) is "true":
    let path = get-folder-path(prompt.Text)
    let res = ask store to "create-folder {path}"
    let success = is-successful-spawn(res)
    emit report(Success: success, Message: "Folder operation completed", CreatedPath: path)

  on prompt where is-microsoft-create-prompt(prompt.Text) is "true":
    let path = get-folder-path(prompt.Text)
    let res = ask store to "create-folder {path}"
    let success = is-successful-spawn(res)
    emit report(Success: success, Message: "Folder operation completed", CreatedPath: path)

scenario "Scenario 1: Handles CreateFolderRequest successfully"
  given is-d-drive-prompt(folder.Prompt) is "true"
  given is-microsoft-create-prompt(folder.Prompt) is "false"
  given get-folder-path(folder.Prompt) is "D:/digitalbrain-sandbox"
  given store returns "success:D:/digitalbrain-sandbox"
  when synapse folder(Prompt: "Create a folder on D drive named D:/digitalbrain-sandbox")
  then synapse report emitted with Success == "true"
  and synapse report emitted with CreatedPath == "D:/digitalbrain-sandbox"

scenario "Scenario 2: Intercepts UserPromptReceived for folder creation"
  given is-d-drive-prompt(prompt.Text) is "false"
  given is-microsoft-create-prompt(prompt.Text) is "true"
  given get-folder-path(prompt.Text) is "D:/my-folder"
  given store returns "success:D:/my-folder"
  when synapse prompt(Text: "Microsoft.Windows.CreateFolder D:/my-folder")
  then synapse report emitted with Success == "true"
  and synapse report emitted with CreatedPath == "D:/my-folder"
