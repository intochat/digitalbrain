Feature: Memory
  Notes are remembered and forgotten through scheduled reactions.

  Scenario: A remembered note is recalled
    Given a running brain
    When "claude" remembers note "tea" with text "I prefer green tea" in namespace "notes" on memory "personal"
    Then the memory command is accepted
    And "claude" waits up to 5 seconds until memory "personal" recalls key "tea" with text "I prefer green tea" for query "I prefer green tea" in namespace "notes"

  Scenario: A forgotten note is no longer recalled
    Given a running brain
    When "claude" remembers note "tea" with text "I prefer green tea" in namespace "notes" on memory "personal"
    Then the memory command is accepted
    And "claude" waits up to 5 seconds until memory "personal" recalls key "tea" with text "I prefer green tea" for query "I prefer green tea" in namespace "notes"
    When "claude" forgets note "tea" in namespace "notes" on memory "personal"
    Then the memory command is accepted
    And "claude" waits up to 5 seconds until memory "personal" recalls no matches for query "I prefer green tea" in namespace "notes"

  Scenario: The reserved namespace is refused
    Given a running brain
    When "claude" remembers note "tea" with text "I prefer green tea" in namespace "digitalbrain.capabilities" on memory "personal"
    Then the memory command fails with "reserved"
