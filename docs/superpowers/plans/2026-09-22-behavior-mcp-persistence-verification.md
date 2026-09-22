# Behavior MCP persistence verification

Created through the running IntoChat workspace MCP endpoint on 2026-09-22, in workspace `b97ad807-ca65-4536-8151-b5da0a906692` (Untitled workspace).

| Behavior | ID | Draft | Validation |
| --- | --- | --- | --- |
| Clock status | demo-clock-status | 1 | Passed, 2/2 tests |
| Reminder status | demo-reminder-status | 1 | Passed, 2/2 tests |

Both are saved, validated drafts, not deployed workers. Each listens to its own demo timer and writes the timer identity and timestamp to its own IText neuron. Existing user entries were preserved.

The test used MCP initialize, tools/list, code_contracts, behavior_describe, code_draft_read/save/check, code_check_read, and behavior_details. A separate read of the application's catalog endpoint confirmed both entries are discoverable by the Behaviors screen.

Direct invocation initially failed with `Unable to resolve service for type System.String while attempting to activate IntoChat.ScopedBehaviorTools`. Generic WithTools registration constructed the target instead of using the registered workspace-scoped factory. Registration now uses an explicit per-invocation target factory resolving ScopedBehaviorTools from request services. The application rebuilt successfully, and the same MCP calls succeeded afterward.

After a full Aspire stop/start, a new MCP session recovered both names, draft sources, tests, passing check results and artifact references. Source hashes were unchanged:

- Clock: `728cae90607a256e17f20cf2b9b1ce7c6382d92630c1a85e654714d7ac8288fe`
- Reminder: `c2879df95c427e0f7b989ec11e88f2895fce22f671b82385eee45ba48d95515e`

Both catalog JSON files were independently observed under the application's `.digitalbrain/workspace/behaviors/items` directory. Drafts and verified artifacts remain under the configured `.digitalbrain/behavior-runtime/coding` directory.

The reminder's initial simultaneous build failed with an empty build diagnostic; a separate validation attempt passed without source changes. That transient failure remains in check history. Restarting only the backend also encountered stale Orleans membership; a full Aspire restart recovered normally.

Native UI interactions were not used in this verification. Catalog visibility was verified through the same endpoint consumed by the UI.
