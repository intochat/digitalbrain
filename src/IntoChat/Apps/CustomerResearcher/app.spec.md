# Customer Researcher

Small signal-driven scenarios compose a researcher window, submit commands and verify company details against observed pages. UI buttons and assistant tools invoke the same operations. The installed app owns its Postgres table across behavior files and upgrades.

### Show the researcher

When the open operation arrives, compose the window and return its surface.

### Submit UI commands

When Research or Stop is clicked, invoke the corresponding app operation.

### Research and store evidence

When research is requested, run the bounded browser/model algorithm and save verified evidence. Stop cancels the current run; result reads the stored company.

Production research uses the host's default LLM. Legacy Model settings do not pin a provider.
Scratch scenario installs may supply scripted/<key> as Model to run deterministic checks.

## Scenario: Opening the researcher composes its surface

Invoking "open" answers a window whose surface is named and titled "Customer Researcher", with the
company field, the research and stop buttons, the status text and the browser paired to its driver.

## Scenario: Stopping an idle researcher is safe

Invoking "stop" with nothing running answers "Stopped." and does not fail.

## Scenario: Research verifies the company against observed pages and saves it

Invoking "research" with a company name drives the browser from a search page to the official
site, extracts details with the model, keeps only values found verbatim on observed official
pages, and upserts the row — keyed by the query, so a retry updates the same record. "result"
answers the stored row with its evidence.

## Scenario: An unverifiable company is refused and saves nothing

When no official page can be observed, research answers a refusal and no row is stored.

## Scenario: Stop during research cancels it or the research completes, never half-saves

A stop raced against a running research either cancels it ("Stopped.", nothing stored) or loses
the race to a completed save; there is no half-written state either way.

## Scenario: Research runs live (live)

With a connected window browser and a real model, "research" drives the visible browser and saves
findings to Postgres. Live scenarios document the app; they are not part of the deterministic gate.
