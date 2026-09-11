Feature: agent
  An agent is a Session neuron with a model attached. It runs a MAF agent over the
  provider named by Instruct, with the four brain operations as tools.

  Scenario: Instruct resolves a Gmail native tool by its plain name
    Given a running brain with AI and fake Google
    When the Gmail account "reader@example.com" connects
    Then the Gmail connection reports "reader@example.com"
    Given the scripted model will call tool "search_gmail_threads" with {"query":"from:vlad@intochat.io"} then say "found the customer"
    When session "claude" fires "Instruct" {"tools":["search_gmail_threads"]} at "agent:a"
    And session "claude" fires "Ask" {"text":"find the customer email"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a tool result containing "thread-intochat"
    And reading "claude" shows latest "Reply" {"text":"found the customer"}

  Scenario: Native tool prompt injection is refused before reaching the model
    Given a running brain with AI
    And the scripted model will call tool "external_content" with {} then say "content refused"
    When session "claude" fires "Instruct" {"tools":["external_content"]} at "agent:a"
    And session "claude" fires "Ask" {"text":"read external content"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a tool result containing "External content refused."
    And the scripted model received no tool result containing "ignore all previous instructions"
    And reading "claude" shows latest "Reply" {"text":"content refused"}

  Scenario: Ask is answered with Reply on the same correlation
    Given a running brain with AI
    And the scripted model will say "pong"
    When session "claude" fires "Ask" {"text":"ping"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"pong"}
    And the latest "claude" incoming entry has the same correlation as the latest "claude" outgoing entry

  Scenario: Instruct is the system prompt and provider
    Given a running brain with AI
    And the scripted model will say "ok"
    When session "claude" fires "Instruct" {"provider":"scripted","system":"You are terse."} at "agent:a"
    And session "claude" fires "Ask" {"text":"hi"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a system message "You are terse."

  Scenario: The agent uses a brain tool and it shows in its journals
    Given a running brain with AI
    And session "claude" fires "Note" {"text":"deploy on fridays"} at "policy"
    And the scripted model will call tool "read" with {"neuron":"policy","what":"state"} then say "fridays"
    When session "claude" fires "Ask" {"text":"when do we deploy?"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model received a tool result containing "deploy on fridays"
    And reading "claude" shows latest "Reply" {"text":"fridays"}

  Scenario: The agent fires through a tool and the fire is journaled on the agent
    Given a running brain with AI
    And the scripted model will call tool "fire" with {"type":"Note","body":"{\"text\":\"remembered\"}","to":"memo"} then say "done"
    When session "claude" fires "Ask" {"text":"remember this"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then "agent:a" outgoing journal contains "Note" {"text":"remembered"}
    And "memo" incoming journal contains "Note" {"text":"remembered"}

  Scenario: Two conversations do not share a session
    Given a running brain with AI
    And the scripted model will say "one"
    And the scripted model will say "two"
    When session "claude" fires "Ask" {"text":"a"} at "agent:a"
    And session "bob" fires "Ask" {"text":"b"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    And "bob" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model saw 2 conversations with 1 user message each

  Scenario: An unknown provider is a Reply that says what to configure
    Given a running brain with AI
    When session "claude" fires "Instruct" {"provider":"nope"} at "agent:a"
    And session "claude" fires "Ask" {"text":"hi"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the latest "claude" incoming "Reply" text contains "Provider 'nope' is not configured"

  Scenario: Instruct names the model the provider is asked for
    Given a running brain with AI
    And the scripted model will say "ok"
    When session "claude" fires "Instruct" {"provider":"scripted","model":"m-two"} at "agent:a"
    And session "claude" fires "Ask" {"text":"hi"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the scripted model was asked with model "m-two"

  Scenario Outline: A model that times out answers with a Reply and the agent keeps working
    Given a running brain with AI
    And the scripted model will time out with "<failure>"
    And the scripted model will say "after"
    When session "claude" fires "Ask" {"text":"first"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    Then the latest "claude" incoming "Reply" text contains "timed out"
    When session "claude" fires "Ask" {"text":"second"} at "agent:a"
    And "claude" waits up to 10 seconds for 2 incoming "Reply"
    Then reading "claude" shows latest "Reply" {"text":"after"}

    Examples:
      | failure               |
      | TaskCanceledException |
      | TimeoutException      |

  Scenario: A second Ask on the same correlation continues the conversation
    Given a running brain with AI
    And the scripted model will say "one"
    And the scripted model will say "two"
    When session "claude" fires "Ask" {"text":"first"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    And session "claude" fires "Ask" {"text":"second"} at "agent:a" with the correlation of its last fire
    And "claude" waits up to 10 seconds for 2 incoming "Reply"
    Then the scripted model's last request contained 2 user messages

  Scenario: Instruct names a neuron and its typed methods become tools
    Given a running brain with AI
    And the scripted model will call tool "counter_c_add" with {"count":2} then say "added"
    And the scripted model will call tool "counter_c_add" with {"count":2} then say "added again"
    When session "claude" fires "Instruct" {"tools":["counter:c"]} at "agent:a"
    And session "claude" fires "Ask" {"text":"add two"} at "agent:a"
    And "claude" waits up to 10 seconds for an incoming "Reply"
    And session "claude" fires "Ask" {"text":"add two again"} at "agent:a"
    And "claude" waits up to 10 seconds for 2 incoming "Reply"
    Then "c" commands journal shows 2 distinct commands, each Attempted then Completed
    And "claude" waits up to 20 seconds until counter "c" total is 4
