# IntoChat test cleanup verification

Phase 1 ran directly on `perf/e2e-shared-apphost` at the user's request.

- New packaging test first failed on Dockerfile/Container.pubxml module-set mismatch, then passed after aligning the publish profile.
- IntoChat Unit: 32 passed after cleanup (baseline: 58 passed; the reported three old failures did not reproduce).
- Compute Unit: 37 passed; Assistant Unit: 54 passed; Core Unit: 150 passed.
- Files Unit: 8 passed; Flutter Unit: passed.
- Moved Flutter shell HTTP fact: 1 passed; CSharp authoring composition E2E fact: 1 passed.
- Identity Unit: 23 passed; IntoChat E2E compiled with zero warnings/errors. Its full suite was skipped as requested.
- `aspire run --detach --non-interactive` from IntoChat/AppHost, followed by starting the on-demand sandbox and checking `aspire describe --format Json`: all 52 resources Healthy. The smoke host was stopped afterward.

Concurrent project builds hit shared-output Windows locks; subsequent builds ran sequentially. The repository's codegraph sync target emitted an unrelated warning in some runs. Existing and concurrent master-key changes were excluded from the cleanup commits.
