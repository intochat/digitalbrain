Feature: Enrich Salesforce
  Enrich Salesforce turns one Gmail message and public web evidence into one reviewable Salesforce field update.

  Background:
    Given a clean Feature scenario

  Scenario: Enrich the single matching Salesforce account
    Given the Gmail message is from "priya@northstarrobotics.example" about "Pilot rollout"
    And Salesforce has one account named "Northstar Robotics"
    And web search returns evidence "Northstar Robotics builds warehouse automation systems."
    When the Gmail received input is handled
    Then the Feature execution succeeds
    And exactly one Salesforce Description update is proposed for "Northstar Robotics"
    And the proposal contains "warehouse automation systems"

  Scenario: Refuse to update when no Salesforce account matches
    Given the Gmail message is from "priya@northstarrobotics.example" about "Pilot rollout"
    And Salesforce has no matching account
    And web search returns evidence "Northstar Robotics builds warehouse automation systems."
    When the Gmail received input is handled
    Then the Feature execution fails with "No Salesforce account matched Northstar Robotics."
    And no Salesforce update is proposed

  Scenario: Refuse to update when the Salesforce account is ambiguous
    Given the Gmail message is from "priya@northstarrobotics.example" about "Pilot rollout"
    And Salesforce has two accounts named "Northstar Robotics"
    And web search returns evidence "Northstar Robotics builds warehouse automation systems."
    When the Gmail received input is handled
    Then the Feature execution fails with "Salesforce account matching for Northstar Robotics is ambiguous."
    And no Salesforce update is proposed
