Feature: Standing composition
  Standing behavior is a synapse: a sensory neuron fires, and whoever is connected hears it.
  Deterministic glue is a recipe neuron. Its Instruct says which contracts to call.
  Judgment is an agent. One-shot work is describe/call and then it is done.
  Tests simulate a webhook by firing from the source, the same way a Gmail or X neuron would.

  Scenario: A topic walks to working-style notes
    Given a running brain
    When session "claude" fires "Note" {"text":"always show a table"} at "show-table"
    And "leads" is connected to "show-table" for "Note"
    And the synapses of "leads" for "Note" are followed
    Then the walk visited "show-table"
    And reading "show-table" shows latest "Note" {"text":"always show a table"}

  Scenario: Every Elon post appends the current bitcoin price to a chart
    Given a running brain with UI
    And session "claude" fires "Quote" {"usd":64000} at "bitcoin"
    And session "claude" fires "Instruct" {"on":"Posted","read":"bitcoin","state":"Quote","field":"usd","call":{"neuron":"chart:elon-btc","interface":"ui.chart","method":"append","args":{"title":"Elon / BTC","point":{"label":"elon","value":"$read","eventId":"$signalId"}}}} at "recipe:elon-btc"
    And "elon" is connected to "recipe:elon-btc" for "Posted"
    When "elon" fires "Posted" {"text":"to the moon"}
    And "claude" waits up to 10 seconds until "recipe:elon-btc" pending count is 0
    And "claude" waits up to 10 seconds until "chart:elon-btc" pending count is 0
    Then chart "elon-btc" contains "Elon / BTC" with point "elon" valued 64000

  Scenario: Disconnect stops the standing rule
    Given a running brain with UI
    And session "claude" fires "Quote" {"usd":64000} at "bitcoin"
    And session "claude" fires "Instruct" {"on":"Posted","read":"bitcoin","state":"Quote","field":"usd","call":{"neuron":"chart:elon-btc","interface":"ui.chart","method":"append","args":{"title":"Elon / BTC","point":{"label":"elon","value":"$read","eventId":"$signalId"}}}} at "recipe:elon-btc"
    And "elon" is connected to "recipe:elon-btc" for "Posted"
    And "elon" is disconnected from "recipe:elon-btc" for "Posted"
    When "elon" fires "Posted" {"text":"to the moon"}
    Then the fire reached 0 neurons
    And chart "elon-btc" has 0 points

  Scenario: Each posted text appends its letter count to a chart
    Given a running brain with UI
    And session "claude" fires "Instruct" {"on":"Posted","length":"text","call":{"neuron":"chart:letters","interface":"ui.chart","method":"append","args":{"title":"Letter count","point":{"label":"mail","value":"$length","eventId":"$signalId"}}}} at "recipe:letters"
    And "inbox" is connected to "recipe:letters" for "Posted"
    When "inbox" fires "Posted" {"text":"ab"}
    And "claude" waits up to 10 seconds until "recipe:letters" pending count is 0
    And "claude" waits up to 10 seconds until "chart:letters" pending count is 0
    Then chart "letters" contains "Letter count" with point "mail" valued 2

  Scenario: A posted signal is asked of an agent
    Given a running brain with AI
    And session "claude" fires "Instruct" {"on":"Posted","ask":"agent:desk","textField":"text"} at "recipe:mail"
    And "inbox" is connected to "recipe:mail" for "Posted"
    And the scripted model will say "Elon posted about rockets"
    When "inbox" fires "Posted" {"text":"rockets"}
    And "recipe:mail" waits up to 10 seconds for an incoming "Reply"
    Then reading "recipe:mail" shows latest "Reply" {"text":"Elon posted about rockets"}

  Scenario: One-shot call renders a chart without a recipe
    Given a running brain with UI
    When "claude" calls "ui.chart" "render" on "chart:once" with {"title":"Once","chartKind":"line","points":[{"label":"t0","value":1}]}
    And "claude" waits up to 10 seconds until "chart:once" pending count is 0
    Then chart "once" contains "Once" with point "t0" valued 1
