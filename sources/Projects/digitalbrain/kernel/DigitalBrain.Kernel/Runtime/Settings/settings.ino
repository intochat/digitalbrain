neuron DigitalBrain.Kernel.Settings.SettingsNeuron
  "Hosts the central system settings registry for DigitalBrain.Global and Ino assistant."

  using read        = synapse(DigitalBrain.Runtime.Runtime.Settings.RequestSetting)
  using update      = synapse(DigitalBrain.Runtime.Runtime.Settings.UpdateSetting)
  using result      = synapse(DigitalBrain.Settings.SettingResult)
  using change      = synapse(DigitalBrain.Settings.SettingChanged)

  using readPrivate  = synapse(DigitalBrain.Kernel.Settings.RequestPrivateSetting)
  using updatePrivate = synapse(DigitalBrain.Kernel.Settings.UpdatePrivateSetting)
  using privateRes   = synapse(DigitalBrain.Kernel.Settings.PrivateSettingResult)

  using requestCard = synapse(DigitalBrain.Runtime.Runtime.Settings.RequestSettingsCard)
  using card        = synapse(DigitalBrain.Runtime.Ui.RfwCard)

  using store       = neuron(DigitalBrain.Kernel.Settings.SettingsStore)

  # Public read/write paths
  on read:
    let val = ask store to "get {read.Scope}:{read.Key}"
    emit result(Scope: read.Scope, Key: read.Key, Value: val)

  on update:
    let ok = ask store to "set {update.Scope}:{update.Key}={update.Value}"
    emit change(Scope: update.Scope, Key: update.Key, Value: update.Value)

  # Restricted paths gated by token validation
  on readPrivate where is-valid-token(readPrivate.Token) is "true":
    let val = ask store to "get-private {readPrivate.Scope}:{readPrivate.Key}"
    emit privateRes(Scope: readPrivate.Scope, Key: readPrivate.Key, Value: val)

  on updatePrivate where is-valid-token(updatePrivate.Token) is "true":
    let ok = ask store to "set-private {updatePrivate.Scope}:{updatePrivate.Key}={updatePrivate.Value}"

  on requestCard:
    let cardData = ask store to "settings-card"
    emit card(LibraryName: "digitalbrain", RootWidget: "SettingsCard", DataJson: cardData, ReceiverNeuronType: "HomeFeed")

scenario "Scenario 1: Public setting read"
  given store returns "dark"
  when synapse read(Scope: "user", Key: "theme")
  then synapse result emitted with Value == "dark"

scenario "Scenario 2: Public setting update emits change signal"
  given store returns "ok"
  when synapse update(Scope: "user", Key: "theme", Value: "light")
  then synapse change emitted with Value == "light"

scenario "Scenario 3: Private setting read succeeds with valid token"
  given is-valid-token(readPrivate.Token) is "true"
  given store returns "sk-12345"
  when synapse readPrivate(Token: "admin-session-token", Scope: "user", Key: "apiKey")
  then synapse privateRes emitted with Value == "sk-12345"

scenario "Scenario 4: Request settings card returns RFW settings card"
  given store returns "settings-card-data"
  when synapse requestCard()
  then synapse card emitted with LibraryName == "digitalbrain"

