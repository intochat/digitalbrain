# Marketplace extraction verification

Implemented directly on `perf/e2e-shared-apphost`, as requested. Apps now owns authoring contracts, implementation, routes, runtimes, shipping, and availability policy. IntoChat supplies its embedded source and settings package identity. Provider descriptions drive authoring prompts; the optional sandbox contract replaces concrete CSharp authoring dependencies. Configuration uses `DigitalBrain:Apps`.

Final verification:

- Apps Unit: 59 passed, including real draft-index grains for two callers, mapped endpoint invalid-ID rejection, and foreign-brain denial through the membership seam.
- IntoChat Unit: 13 passed after removing the duplicate generic options theory. Injecting an empty Docker module assignment produced the expected failing packaging test; restoring the file returned the suite to green.
- Assistant Unit: 55 passed; CSharp Unit: 69 passed; CSharp E2E: 2 passed, including the sandbox journey.
- IntoChat E2E builds with zero warnings/errors. Its full suite was skipped as requested.
- Flutter E2E: 12 passed. Flutter analyze was clean and Flutter tests passed 107 tests after the draft contract comment updates.
- Final Aspire smoke: all 52 resources Healthy, including the explicitly started on-demand sandbox. The smoke AppHost was stopped afterward.
- Independent specification and standards reviews found packaging parsing, options duplication, and missing endpoint behavior coverage. All three were repaired; follow-up specification review reported no remaining actionable findings.

Persisted aliases, field IDs, enum numbers, and grain keys were retained and inspected. Draft reactivation is covered, but no pre-migration binary serialized fixture was available; the test writes and reads the relocated contracts. Module route authorization tests use real draft grains and an `IBrainAccess` seam; Identity/Core retain membership implementation coverage. Built-in runtimes remain together in their moved source file rather than being split into separate files. Commit grouping follows coherent changes rather than every suggested commit boundary.

The repository codegraph sync target sometimes emitted an unrelated warning. The sandbox E2E run logged transient Orleans timeouts and Aspire shutdown diagnostics but finished successfully. Existing and concurrent master-key changes and untracked `docs/prompts/` were preserved outside this work.
