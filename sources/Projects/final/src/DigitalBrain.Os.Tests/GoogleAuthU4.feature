@GoogleAuth
Feature: Google Auth U4 demo (per-brain isolation, encryption at rest, connector secret store, D3 grant flow)
  Non-high-sev feature (exercised in full runs / dedicated; keeps DistributionDynamicHandlers.feature as pure Core Law N+1 proof with 0 failures).
  Ports the 3 v2 AuthGoogle.ino scenarios + D3 capability grant (install emits request; decision journaled; privileged SaveFile after Allow).

  # (D Task2 vault + Task1 PKCE make these real: per-brain isolation via per-key vault, encryption at-rest via AES-GCM in vault (not plain/XOR), connector (ga/vault) reads the token.)
  Scenario: per-brain token isolation
    Given a clean digital brain
    When the "brain-a" account begins google auth
    And google auth completes for "brain-a" with token hint "ya29.a-aaa"
    Then the decrypted token for "brain-a" is "ya29.a-aaa"
    When the "brain-b" account begins google auth
    And google auth completes for "brain-b" with token hint "ya29.b-bbb"
    Then the decrypted token for "brain-b" is "ya29.b-bbb"
    And the decrypted token for "brain-a" remains "ya29.a-aaa" (isolation)

  # (D vault AES)
  Scenario: encryption at rest
    Given a clean digital brain
    When the "root" account begins google auth
    And google auth completes for "root" with token hint "secret-token-123"
    Then the internal encrypted token for "root" is not plaintext "secret-token-123"
    And the decrypted token for "root" is "secret-token-123"

  # (D vault as the "connector" secret store)
  Scenario: connector reads secret store
    Given a clean digital brain
    When the "root" account begins google auth
    And google auth completes for "root" with token hint "g-token-xyz"
    Then the google auth connector can read the decrypted token for "root"

  # DEFERRED: sub-project D (or E) — full grant request on privileged install + honored Gmail+Save path (request emission timing + exact CapabilityGrantRequest observable in binding; vault makes the token part real).
  @ignore
  Scenario: D3 grant request on privileged bundle install + allow path
    Given a clean digital brain
    When I pack the "google-auth" experience
    And I publish "google-auth" to the local marketplace
    And I install "google-auth" from the marketplace (main brain for grant visibility in this substrate)
    Then a CapabilityGrantRequest for "google-auth" with SaveFileRequest and GoogleApi is emitted
    When the user allows the grant for "google-auth"
    Then a CapabilityDecision Allowed true for "google-auth" is journaled
    When I send GmailLastSendersRequest on main
    Then GmailLastSendersResult is produced and SaveFileRequest is emitted (grant honored)
