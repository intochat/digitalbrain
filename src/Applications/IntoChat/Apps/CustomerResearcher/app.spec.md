# Customer Researcher

Live company research composed entirely from platform neurons: a driven browser (`IPlaywright`
behind the window's `IWebBrowser`), a model, and a scoped Postgres table behind one surface. The
app is two behaviors: the surface composes the window, and the research behavior owns the loop,
Stop and the table. Research and Stop arrive ordered on the app's invocation stream; the surface
buttons raise the same intents. Stop cancels an in-flight research in-process and takes effect at
once; a research request replayed after a restart simply runs again and can be stopped again.

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
