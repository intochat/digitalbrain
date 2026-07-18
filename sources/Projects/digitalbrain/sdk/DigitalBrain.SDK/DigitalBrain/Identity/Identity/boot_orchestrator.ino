neuron DigitalBrain.SDK.Identity.BootOrchestrator
  "Coordinates the startup, onboarding, and login boot sequence."

  using started        = synapse(DigitalBrain.Brain.Started)
  using onboarding     = synapse(DigitalBrain.Domains.Onboarding.Contracts.OnboardingResult)
  using accepted       = synapse(DigitalBrain.Domains.Onboarding.Contracts.PolicyAccepted)
  using loginResult    = synapse(DigitalBrain.SDK.Identity.Contracts.LoginResult)

  using reqOnboarding  = synapse(DigitalBrain.Domains.Onboarding.Contracts.RequestOnboarding)
  using reqLoginCard   = synapse(DigitalBrain.SDK.Identity.Contracts.RequestLoginCard)

  on started:
    emit reqOnboarding(UserId: "local")

  on onboarding where is-equal(onboarding.NeedsAccept) is "false":
    emit reqLoginCard(UserId: "local")

  on accepted:
    emit reqLoginCard(UserId: "local")

  on loginResult where is-equal(loginResult.Success) is "true":
    log "boot: authentication successful for user {loginResult.UserId}."

scenario "Scenario 1: System startup triggers Onboarding terms check"
  when synapse started()
  then synapse reqOnboarding emitted with UserId == "local"

scenario "Scenario 2: OnboardingResult requiring no acceptance transitions directly to Login card"
  given is-equal(onboarding.NeedsAccept) is "false"
  when synapse onboarding(NeedsAccept: "false", CurrentVersion: "2026-05-19")
  then synapse reqLoginCard emitted with UserId == "local"

scenario "Scenario 3: Policy accepted signal transitions to Login card"
  when synapse accepted(UserId: "local", Version: "2026-05-19")
  then synapse reqLoginCard emitted with UserId == "local"
