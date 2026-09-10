Feature: Integrations

  Scenario: Gmail connects and a native read reaches the fake provider
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects
    Then the Gmail connection reports "person@example.com"
    And neither Gmail journal contains the OAuth tokens
    When the "search_threads" Gmail tool runs for "customer"
    Then the Gmail read returns thread "thread-intochat"

  Scenario: A Gmail draft requires the reviewed preview and can be confirmed only once
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects
    Then the Gmail connection reports "person@example.com"
    When a native Gmail draft is prepared
    Then the Gmail preview is returned and only its identifiers are published
    And Gmail refuses a confirmation with another schema
    When the reviewed Gmail draft is confirmed
    Then Gmail reports one created fake draft
    And Gmail refuses another confirmation of the consumed preview

  Scenario: An expired Gmail token is refreshed and the read is retried once
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects with a one-second token
    And the Gmail token lifetime elapses
    Then the Gmail read succeeds after a single refresh

  Scenario: Salesforce connects and a guarded query reaches the fake provider
    Given a running brain with the Salesforce module in fake mode
    When the Salesforce account connects
    Then the Salesforce connection is reported
    When the guarded query "SELECT Id FROM Account WHERE Name = 'intochat' LIMIT 5" runs
    Then the Salesforce query returns 0 records
    When the guarded query "SELECT Id FROM Account" runs
    Then the Salesforce query was refused

  Scenario: A signed GitHub webhook delivery refreshes the bound repository
    Given a running brain with the Microsoft module in fake mode
    When a signed "pull_request" webhook delivery for pull request 42 arrives
    Then the repository reports pull request 42 as open
    When the same signed webhook delivery arrives again
    Then the repository accepted it as a duplicate
