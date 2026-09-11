Feature: Integrations

  Scenario: A permanently refused repository connection reaches the session and drains
    Given a running brain with the Microsoft module in fake mode
    And "repository:fake" is connected to "alice" for "RepositoryRefused"
    When a GitHub connection mismatches its authorized repository binding
    And "alice" waits up to 10 seconds for an incoming "RepositoryRefused"
    Then "alice" incoming journal contains "RepositoryRefused" {"reason":"The connection does not match its authorized binding."}
    When "alice" waits up to 10 seconds until "repository:fake" pending count is 0
    Then "repository:fake" pending count is 0
    And "alice" incoming journal has 1 entries

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
    And no Salesforce journal contains the OAuth tokens
    When the guarded query "SELECT Id FROM Account WHERE Name = 'intochat' LIMIT 5" runs
    Then the Salesforce query returns 0 records
    When the guarded query "SELECT Id FROM Account" runs
    Then the Salesforce query was refused

  Scenario: A Salesforce write requires the reviewed schema and can be confirmed only once
    Given a running brain with the Salesforce module in fake mode
    When the Salesforce account connects
    Then the Salesforce connection is reported
    When a native Salesforce record creation is prepared
    Then the exact Salesforce preview is published without writing
    And Salesforce refuses a confirmation with another schema
    When the reviewed Salesforce write is confirmed twice
    Then Salesforce reports one written record
    And Salesforce refuses another confirmation of the consumed preview

  Scenario: An expired Salesforce token is refreshed and the read is retried once
    Given a running brain with the Salesforce module in fake mode
    When the Salesforce account connects with a one-second token
    And the Salesforce token lifetime elapses
    Then the Salesforce read succeeds after a single refresh

  Scenario: A signed GitHub webhook delivery refreshes the bound repository
    Given a running brain with the Microsoft module in fake mode
    When a signed "pull_request" webhook delivery for pull request 42 arrives
    Then the repository reports pull request 42 as open
    When the same signed webhook delivery arrives again
    Then the repository accepted it as a duplicate

  Scenario: A permanent Gmail preflight rejection leaves the connection usable after recovery
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects
    Then the Gmail connection reports "person@example.com"
    When a native Gmail draft is prepared
    Then the Gmail preview is returned and only its identifiers are published
    When the Gmail preflight rejects the schema and its snapshot loses activation
    Then the Gmail preflight reports uncertainty and a fresh preview can be prepared

  Scenario: A permanent Salesforce preflight rejection leaves the connection usable after recovery
    Given a running brain with the Salesforce module in fake mode
    When the Salesforce account connects
    Then the Salesforce connection is reported
    When a native Salesforce record creation is prepared
    Then the exact Salesforce preview is published without writing
    When the Salesforce preflight rejects the schema and its snapshot loses activation
    Then the Salesforce preflight reports uncertainty and a fresh preview can be prepared

  Scenario: Salesforce schema reads require sign-in and return the connected org metadata
    Given a running brain with the Salesforce module in fake mode
    Then Salesforce offers a secure sign-in card
    When the Salesforce account connects
    Then the Salesforce connection is reported
    And Salesforce schema reads return object names and relationships
