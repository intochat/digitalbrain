neuron DigitalBrain.SDK.Identity.IdentityNeuron
  "Handles user credentials, lockout gates, and session tokens."

  using request  = synapse(DigitalBrain.SDK.Identity.Contracts.RequestLogin)
  using reqCard  = synapse(DigitalBrain.SDK.Identity.Contracts.RequestLoginCard)
  using reqCreate = synapse(DigitalBrain.SDK.Identity.Contracts.RequestCreateBrain)
  using result   = synapse(DigitalBrain.SDK.Identity.Contracts.LoginResult)
  using spawned  = synapse(DigitalBrain.SDK.Identity.Contracts.UserBrainSpawned)
  using createRes = synapse(DigitalBrain.SDK.Identity.Contracts.CreateBrainResult)
  using card     = synapse(DigitalBrain.Runtime.Ui.RfwCard)
  using createRg = synapse(Microsoft.Azure.ResourceGroup.Create)
  using store    = neuron(DigitalBrain.SDK.Identity.IdentityStore)


  on reqCard:
    let cardData = ask store to "login-card {reqCard.UserId}"
    emit card(LibraryName: "digitalbrain", RootWidget: "LoginCard", DataJson: cardData, ReceiverNeuronType: "HomeFeed")

  on reqCreate:
    let res = ask store to "spawn-brain {reqCreate.UserId}:{reqCreate.NewBrainId}:{reqCreate.SourceBrainId}:{reqCreate.SyncTarget}"
    let success = is-successful-spawn(res)
    let token = get-token-from-spawn(res)
    emit createRes(Success: success, BrainId: reqCreate.NewBrainId, SessionToken: token, ErrorMessage: "")
    emit spawned(UserId: reqCreate.UserId, SessionToken: token)
    let isAzure = is-azure(reqCreate.SyncTarget)
    if isAzure:
      emit createRg(ResourceGroupName: reqCreate.NewBrainId, Location: "eastus", SubscriptionId: "")


  on request where is-locked-out(request.Username) is "true":
    emit result(Success: "false", UserId: request.Username, ErrorMessage: "Too many failed attempts. Try again in 30 seconds.")

  on request where is-valid-login("{request.Username}:{request.Password}") is "true":
    let token = ask store to "get-token {request.Username}"
    emit result(Success: "true", UserId: request.Username, SessionToken: token, ErrorMessage: "")
    emit spawned(UserId: request.Username, SessionToken: token)

  on request where is-valid-login("{request.Username}:{request.Password}") is "invalid":
    emit result(Success: "false", UserId: request.Username, ErrorMessage: "Invalid credentials.")

scenario "Scenario 1: Valid login returns success and session token"
  given is-locked-out(request.Username) is "false"
  given is-valid-login("{request.Username}:{request.Password}") is "true"
  given store returns "session-admin-12345"
  when synapse request(Username: "admin", Password: "admin123")
  then synapse result emitted with Success == "true"
  and synapse result emitted with SessionToken == "session-admin-12345"
  and synapse spawned emitted with UserId == "admin"
  and synapse spawned emitted with SessionToken == "session-admin-12345"

scenario "Scenario 2: Invalid login returns failure"
  given is-locked-out(request.Username) is "false"
  given is-valid-login("{request.Username}:{request.Password}") is "invalid"
  when synapse request(Username: "admin", Password: "wrong")
  then synapse result emitted with Success == "false"
  and synapse result emitted with ErrorMessage == "Invalid credentials."

scenario "Scenario 3: Login attempt during lockout is immediately blocked"
  given is-locked-out(request.Username) is "true"
  when synapse request(Username: "admin", Password: "wrong")
  then synapse result emitted with Success == "false"
  and synapse result emitted with ErrorMessage == "Too many failed attempts. Try again in 30 seconds."

scenario "Scenario 4: Requesting login card returns RFW LoginCard signal"
  given store returns "terms-card-data"
  when synapse reqCard(UserId: "local")
  then synapse card emitted with LibraryName == "digitalbrain"
  and synapse card emitted with RootWidget == "LoginCard"

scenario "Scenario 5: Spawning a clean brain successfully"
  given store returns "success:session-clean-12345"
  when synapse reqCreate(UserId: "user123", NewBrainId: "my-clean-brain", SourceBrainId: "")
  then synapse createRes emitted with Success == "true"
  and synapse createRes emitted with BrainId == "my-clean-brain"
  and synapse spawned emitted with UserId == "user123"
  and synapse spawned emitted with SessionToken == "session-clean-12345"

scenario "Scenario 6: Branching a brain successfully"
  given store returns "success:session-branch-12345"
  when synapse reqCreate(UserId: "user123", NewBrainId: "my-dev-branch", SourceBrainId: "primary")
  then synapse createRes emitted with Success == "true"
  and synapse createRes emitted with BrainId == "my-dev-branch"
  and synapse spawned emitted with UserId == "user123"
  and synapse spawned emitted with SessionToken == "session-branch-12345"

