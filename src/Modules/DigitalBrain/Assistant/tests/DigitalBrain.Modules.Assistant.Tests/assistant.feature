Feature: Assistant

  Scenario: The assistant composes its chat
    Given the assistant is started
    Then its surface is titled "Assistant"
    And its surface declares explicit assistant controls

  Scenario: Typed message
    Given the assistant is started
    When I type "hello"
    Then the chat shows my message "hello"
    And the chat shows the assistant reply "You said: hello"

  Scenario: Voice message
    Given the assistant is started
    When I say "hello there" by voice
    Then the chat draft is "hello there"
    When I send the draft
    Then the chat shows my message "hello there"
    And the chat shows the assistant reply "You said: hello there"

  Scenario: Show customers from Supabase
    Given Supabase has a table "customers" with 3 rows
    And the assistant is started
    When I type "show me customers from supabase"
    Then the chat shows the assistant reply "Your customers are open in a table window."
    And the assistant used "supabase_schema" and "show_supabase_query_table"
    And a table window is open in the workspace
    And the table has 3 rows

  Scenario: Opening the main assistant again preserves its messages and draft
    Given the assistant is started
    When I type "hello"
    Then the chat shows the assistant reply "You said: hello"
    When I draft "unfinished"
    And the assistant is started
    Then the chat shows the assistant reply "You said: hello"
    And the chat draft is "unfinished"

  Scenario: Conversations survive opening the application again
    Given the assistant is started
    And conversation "thread-1" has a completed answer "saved answer"
    When the assistant is started
    Then conversation "thread-1" contains the answer "saved answer"
    And conversation "thread-2" is empty
    And another workspace has no turns in conversation "thread-1"

  Scenario: An application configuration survives repeated startup
    Given the assistant is started
    When I configure the assistant instructions as "Be concise."
    And the assistant is started
    Then the assistant turn uses instructions "Be concise."

  Scenario: Turn composition includes retained conversation context
    Given the assistant is started
    Then the assistant includes the summary "Earlier work" in its turn instructions
    And invalid conversation identifiers are rejected
