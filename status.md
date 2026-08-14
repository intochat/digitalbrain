# CoreV2 product migration status

Updated: 2026-08-14

## Outcome

The CoreV2 product migration is complete. `aspire start` launches a journal-first DigitalBrain with persistent storage, an Orleans runtime, a product gateway/MCP server, a local Gemma 4 assistant, and the Flutter module in headless mode by default.

Chat is the product entry point. One durable activity records the user message, assistant plan, selected operations, tool results, Neuron firings, Synapse delivery, assistant response, and terminal status. The same journal incrementally projects the live BrainGraph rendered by Flutter.

## Compiled product graph

`storage -> clustering/reminders/grainstate/journal -> ollama/gemma4-12b -> runtime -> product -> flutter`

- `DigitalBrainResource` is the Aspire aggregate for the brain.
- `journal` is an explicit persistent Azure Blob resource and a required runtime dependency. Runtime startup fails when its connection is absent.
- `DigitalBrain.RuntimeHost` is the only durable execution runtime. The former parallel product runtime and placeholder Behavior, Conversation, Memory, and Scheduling entity modules have been deleted.
- `DigitalBrain.ProductHost` is an Orleans client exposing HTTP, SSE, and MCP views over the durable runtime.
- Ollama runs as a persistent, GPU-enabled module resource with `gemma4:12b`. The image is pinned to `latest` because Gemma 4 rejects the older integration default.
- Orleans client and silo response budgets are ten minutes so local model warmup and long assistant turns do not violate the durable operation contract.
- Flutter belongs to `Modules/UI/Flutter`. Aspire starts its headless Dart host by default, so normal development starts do not create another desktop window. Window and web hosts remain explicit opt-ins.
- Proof, AI, and UI each own their `Contracts`, `Runtime`, and applicable `Aspire.Hosting`/`Flutter` folders.

## Ready modules

| Module | Product capability | Durable evidence |
| --- | --- | --- |
| Proof | Installs a live Synapse and fires a source Neuron through it | `Proof.Wire@1`, `Proof.Run@1`, delivery and assessment journal records |
| AI | Gemma 4 assistant plans and invokes registered operations | model plan, tool selection, tool result, and response journal records |
| UI | Chat orchestration, live journal, and live BrainGraph | `Chat.Send@1`, HTTP/SSE/MCP contracts, Flutter three-panel shell |

## Live MCP acceptance evidence

The acceptance run used `aspire start --isolated --non-interactive` and Aspire's MCP bridge, not direct test doubles or raw HTTP calls.

- Aspire reported `storage`, `clustering`, `reminders`, `grainstate`, `journal`, `ollama`, `gemma4-12b`, `runtime`, `product`, and headless `flutter` healthy/running.
- `aspire mcp tools` discovered seven ProductHost tools, including `digitalbrain_chat`, `digitalbrain_activity_journal`, and `digitalbrain_brain_snapshot`.
- MCP chat activity `8f535f3e-617b-ab04-538a-c0a6f1abdf3a` asked the assistant to wire Proof to assessment and run `mcp-live-v2`.
- The assistant invoked `Proof.Wire@1` and `Proof.Run@1` and returned both durable tool results.
- The MCP journal returned 23 ordered records. They include `Chat.UserMessage@1`, `Assistant.ModelPlan@1`, both tool selections/results, `BrainGraph.SynapseInstalled@1`, the source Neuron emission, Synapse delivery, assessment Neuron receipt/emission, assistant response, and `Chat.Send@1` completion.
- The MCP BrainGraph returned two Neurons and live Synapse `602ab2e8-02cc-41f9-910a-04786a56b26c` with revision 1, usage count 1, and the chat activity as provenance.
- The MCP activity view reported `Chat.Send@1` as `Completed` at journal sequence 23.
- The headless Flutter log observed the same transition: `BrainGraph sequence=2 neurons=2 synapses=1`. Aspire launched `dart run bin/digitalbrain_host.dart`; no Flutter desktop window was started.

## Verification

- `dotnet build DigitalBrain.slnx --no-restore`: succeeded with 0 warnings and 0 errors.
- `dotnet test DigitalBrain.slnx --no-build --no-restore`: 299 passed, 0 failed, 0 skipped after all live-run fixes.
- Dart core: `dart analyze` clean; 3 tests passed.
- Flutter shell: `flutter analyze` clean; 1 widget test passed.
- Live Aspire/MCP acceptance: passed as documented above.

## Remaining boundary

The V1 `src/Kernel` and `src/Modules` trees remain as uncompiled reference material. Architecture tests prevent active CoreV2 projects from depending on them. They are not part of the CoreV2 build or runtime graph.
