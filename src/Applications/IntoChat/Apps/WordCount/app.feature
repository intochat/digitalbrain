Feature: Word count
  Counts the words in whatever text you give it.

  Scenario: Words are separated by spaces
    When I ask "the quick brown fox"
    Then the answer is "4 words"

  Scenario: Extra spaces do not count as words
    When I ask "  hello    world  "
    Then the answer is "2 words"

  Scenario: One word is singular
    When I ask "hello"
    Then the answer is "1 word"
