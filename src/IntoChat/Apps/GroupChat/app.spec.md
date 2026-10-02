# Group chat

Luna and Gemma brainstorm your question in turns, each building on what was said, and stop as soon
as both agree. Luna then writes the answer they converged on. The "LunaModel" and "GemmaModel"
settings choose who speaks; "MaxRounds" limits the discussion.

## Scenario: The models take turns and stop once they agree

With Luna scripted to reply "A smart dog bowl.", then "AGREE: the bowl with a feeding log.", then
"A smart dog bowl that logs every meal.", and Gemma scripted to reply "Add a feeding log to the
bowl.", then "AGREE: bowl plus log.": asking "Name one product idea for dog owners" makes Luna and
Gemma alternate starting with Luna, tells Gemma what Luna said, ends the discussion agreed after 2
rounds, and answers "A smart dog bowl that logs every meal.".

## Scenario: Without agreement the discussion stops at the round limit

With Luna scripted to reply "A leash.", "A collar.", "A leash with a collar." and Gemma scripted to
reply "Not a leash.", "Not a collar.", and "MaxRounds" set to "2": asking "Pick one accessory"
ends after 2 rounds without agreement and answers "A leash with a collar.".

## Scenario: Luna and Gemma converge on a real answer (live)

With the default models, asking for a product idea makes the two take turns and converge within 3
rounds on one concrete proposal. Live scenarios document the app with real models; they are not
part of the deterministic gate.
