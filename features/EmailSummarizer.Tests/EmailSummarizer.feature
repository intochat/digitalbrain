Feature: Email Summarizer
  Email Summarizer turns one granted Gmail message into one deterministic text surface.

  Background:
    Given a clean Feature scenario

  Scenario: Summarize a granted Gmail message
    Given Gmail message "message-1" has subject "Quarterly launch" and body "The launch is Friday at noon."
    And Gmail message reads are granted
    And model workflow "email-summary" for input "input-1" returns "Launch is Friday at noon."
    When feature input "input-1" requests a summary of Gmail message "message-1"
    Then the Feature execution succeeds
    And exactly one text surface intent contains "Launch is Friday at noon."
    And the Gmail reader and model workflow each ran once
    And the model and surface use distinct stable operation keys

  Scenario: Missing Gmail grant denies execution
    Given Gmail message "message-3" has subject "Private" and body "Restricted content."
    And Gmail message reads are not granted
    When feature input "input-3" requests a summary of Gmail message "message-3"
    Then the Feature execution is denied with "google.gmail.message.read.v1"
    And no model workflow or surface intent ran

  Scenario: Missing model response fails loudly
    Given Gmail message "message-4" has subject "Unconfigured" and body "No response is configured."
    And Gmail message reads are granted
    When feature input "input-4" requests a summary of Gmail message "message-4"
    Then the Feature execution fails with "No model response configured for email-summary."
    And no surface intent was emitted
