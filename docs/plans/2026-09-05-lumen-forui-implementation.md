# Lumen implementation

Approved by the user on 2026-09-05: Lumen visual direction and Forui as the Flutter UI kit foundation.

The default workspace is a warm, light graph with icon neurons, module regions, an animated Ino presence, and a compact conversation at the bottom. Full conversation and existing supporting capabilities remain reachable. The same chat state owns accepted command IDs, streaming, durable journal reconciliation, cancellation, and actionable cards in both presentations.

The graph reads an authenticated projection of current-chat participants and reachable, stored source-owned synapses. It does not manufacture edges for direct method calls. Snapshot timestamps, scope, truncation, loading, and connection failures remain visible. Neuron and edge inspectors expose bounded metadata and safe journal previews. Subscription controls use existing kernel Subscribe/Unsubscribe and wait for authoritative confirmation.

Implementation order: Forui/theme primitives and compact chat in parallel; authenticated graph projection; typed client and lifecycle-safe polling; Lumen shell and graph; focused transport, graph, subscription, and responsive tests; build and native smoke test against Aspire. Existing simulation examples remain explicitly labeled and separate from the live graph.

Rive artwork is a follow-up asset decision. The first implementation uses a small programmatic Ino face, with reduced-motion support; other nodes use provider/capability icons and labels.

## Validation

- Core/client: 22 tests passed, including the new brain HTTP contract and existing SSE acceptance/error handling.
- Shell: 37 unique tests passed across chat, workspace, cards, graph projection, inspectors, parallel/reverse/self edges, subscription confirmation, and examples. The final hint-height adjustment passed five focused cases.
- Forui kit: 3 tests passed for controls, draft ownership, and reduced motion.
- Backend: 8 tests passed, including a real isolated AppHost HTTP subscribe/unsubscribe round trip, no Learned remnant for the removed signal, and foreign-principal rejection. Build completed without warnings or errors.
- Native Windows: sent `hi` in the bottom composer and received a real answer; opened node/edge inspectors and the directory; bound Ino -> main chat for Note; removed that subscription and observed confirmation plus edge removal; opened history and returned with Escape. Restored prior history and provider action cards remained present.
- Aspire: Flutter, kernel, MCP, and scripting worker are Running/Healthy after verification. The native app remains open.

The whole Flutter analysis found no type errors. Its unused import and brace-style findings were corrected; final targeted analysis of the six affected files passed with no issues (`--fatal-infos`). `git diff --check` passes.
