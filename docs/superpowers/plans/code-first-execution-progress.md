# Execution ledger — code-first-composition and agent-data-window

Plans: 2026-09-20-code-first-composition.md; 2026-09-20-agent-data-window.md.
Approved design: option A; interactive table window in the existing workspace.
Base: 3b419529 on archv2. No push authorized.

Ruling: execute in the clean archv2 checkout — the user explicitly moved work here and deleted the previous worktree — avoids another branch/merge workflow; preserve all existing stashes.
Ruling: use this tracked ledger with native PowerShell commands instead of the skills' bash scratch scripts — Windows environment; same persistent task/test record.
Pre-flight: composition tasks 1–3 share typed module contracts and explicit-member patches; default-value assignments must survive overrides.
Pre-flight: tasks 2–4 share deferred resource materialization; both runtime and client WithReference must freeze once.
Pre-flight: window tasks 1–3 share stable table/window IDs, operation receipts and revisions; never reopen a closed window on duplicate result replay.
Pre-flight: window tasks 3–4 share trusted scope/run/call context; tool factories must be per invocation, not cached request closures.
Pre-flight: window tasks 2/5/6 share current Dart table contract and server-scoped routes; preserve paging/filter types and local geometry.

Composition task 1: in progress.

Composition task 1: complete. RED: missing new interfaces (composition-task1-red.log). GREEN: framework 38/38 (composition-task1-green.log). Ruling: add a generic module-contract base for copy/explicit-member JSON patch mechanics; concrete module contracts still own allowed members and mapping. Baseline framework 32/32.
Composition task 2: complete. RED: missing typed module methods (composition-task2-red.log). GREEN: framework 42/42 (composition-task2-green.log), including deferred Postgres and client-first Web finalization. AI optional endpoint/profile fields are compiled explicitly; generated provider secrets remain Aspire parameter references. Ruling: use a distinct supabase-database resource name and supabase-connection parameter name to avoid collision with the module resource; external connection lookup retains its configured name.
