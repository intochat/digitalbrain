Feature: Integrations

  Scenario: Gmail connects and a native read reaches the fake provider
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects
    Then the Gmail connection reports "person@example.com"
    When the "search_threads" Gmail tool runs for "customer"
    Then the Gmail read returns thread "thread-intochat"

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
