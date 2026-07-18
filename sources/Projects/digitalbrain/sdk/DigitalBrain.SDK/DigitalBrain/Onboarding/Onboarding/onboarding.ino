neuron DigitalBrain.Domains.Onboarding.OnboardingNeuron
  "Gates first use of the system on policy acceptance."

  using request  = synapse(DigitalBrain.Domains.Onboarding.Contracts.RequestOnboarding)
  using accept   = synapse(DigitalBrain.Domains.Onboarding.Contracts.AcceptPolicy)
  using result   = synapse(DigitalBrain.Domains.Onboarding.Contracts.OnboardingResult)
  using card     = synapse(DigitalBrain.Runtime.Ui.RfwCard)
  using accepted = synapse(DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted)
  using store    = neuron(DigitalBrain.Domains.Onboarding.OnboardingStore)
  using settings = neuron(DigitalBrain.Kernel.Settings.SettingsStore)

  on request where is-current-version(request.UserId) is "false":
    let currentVersion = ask settings to "get terms-version"
    emit result(NeedsAccept: "true", CurrentVersion: currentVersion)
    let cardData = ask store to "terms-card {currentVersion}"
    emit card(LibraryName: "digitalbrain", RootWidget: "OnboardingCard", DataJson: cardData, ReceiverNeuronType: "HomeFeed")

  on request where is-current-version(request.UserId) is "true":
    let currentVersion = ask settings to "get terms-version"
    emit result(NeedsAccept: "false", CurrentVersion: currentVersion)

  on accept:
    let confirmation = ask store to "accept {accept.UserId} {accept.Version}"
    emit accepted(UserId: accept.UserId, Version: accept.Version)

scenario "Scenario 1: First-use requests prompt with Terms card and return NeedsAccept true"
  given is-current-version(request.UserId) is "false"
  given settings returns "2026-05-19"
  given store returns "terms-card-data"
  when synapse request(UserId: "local")
  then synapse result emitted with NeedsAccept == "true"
  and synapse card emitted with LibraryName == "digitalbrain"

scenario "Scenario 2: Policy acceptance records the signature, subsequent requests return NeedsAccept false"
  given is-current-version(request.UserId) is "true"
  given settings returns "2026-05-19"
  when synapse request(UserId: "local")
  then synapse result emitted with NeedsAccept == "false"
