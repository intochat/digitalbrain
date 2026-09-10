Feature: Integrations

  Scenario: Gmail connects and a native read reaches the fake provider
    Given a running brain with the Google module in fake mode
    When the Gmail account "person@example.com" connects
    Then the Gmail connection reports "person@example.com"
    When the "search_threads" Gmail tool runs for "customer"
    Then the Gmail read returns thread "thread-intochat"
