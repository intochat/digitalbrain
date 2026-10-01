# Customer Researcher

Live company research over the workspace's researcher neuron: a driven browser, an agent and a
Postgres store behind one surface. The app is the researcher's marketplace face; the neuron owns
the browser session and the store.

## Scenario: Opening the researcher composes its surface

Invoking "open" answers a window whose surface is named and titled "Customer Researcher".

## Scenario: Stopping an idle researcher is safe

Invoking "stop" with nothing running answers "Stopped." and does not fail.

## Scenario: Research runs live (live)

Invoking "research" with a company name drives the browser and saves findings to Postgres. Live
scenarios document the app with a real browser and model; they are not part of the deterministic
gate.
