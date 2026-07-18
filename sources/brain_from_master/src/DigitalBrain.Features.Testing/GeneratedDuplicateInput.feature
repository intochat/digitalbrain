@generated-duplicate
Feature: Generated duplicate input
  Every Feature must prove that delivering the same input twice reaches its handler once.

  Scenario: Duplicate delivery commits once
    When the generated Feature input is delivered twice
    Then the generated duplicate gate succeeds
