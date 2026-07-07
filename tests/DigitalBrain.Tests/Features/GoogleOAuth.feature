Feature: Google OAuth for Gmail via INO

  As a user
  I want to authenticate with Google via INO
  So that I can retrieve my recent Gmail senders and messages

Scenario: INO triggers Google auth form when no credentials
  Given the system is running
  When INO receives prompt "show my last 5 gmail senders"
  Then a Google credential form surface is delivered

Scenario: Google auth flow emits AuthUrl signal with correct params
  Given a Google auth neuron
  When AuthRequested signal is delivered
  Then GoogleAuthUrl signal is emitted with offline consent and gmail.readonly scope

Scenario: Full Gmail read after simulated auth
  Given Google credentials are seeded in pack config
  When INO requests gmail messages
  Then Gmail messages are fetched and response emitted