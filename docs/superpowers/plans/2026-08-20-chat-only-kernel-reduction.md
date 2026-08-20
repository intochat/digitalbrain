# Chat-only kernel reduction plan

**Goal:** Convert the retained .NET product surface to a small chat-and-voice system: Flutter continues to call the Kernel; MCP continues to use Client to send chat; Qdrant and Aspire remain. Remove graph/capability/identity support code and non-chat MCP surfaces even where they are currently wired.

**Explicitly retained:** all Flutter sources and windowing tab, UI/voice HTTP paths, `DigitalBrain.Client`, `DigitalBrain.Mcp` as a chat-only surface, Aspire, Qdrant, and test projects/framework.

**Explicitly removed from the public product surface:** MCP journal/transcript/schedule/memory introspection; MCP chat button activation; MCP chart/button result DTOs; Orleans incoming/outgoing/owner-bound capability filters; global Abstractions import files.

**Strategy:** Work in small compilable commits. First make the MCP contract chat-only. Next remove the three runtime filters and their registration, proving Flutter chat and MCP chat stay live. Only then peel the now-unreachable capability, ownership, identity, and global-import support code, updating retained module code to use explicit imports where necessary.

## Task 1: Lock the chat-only MCP contract

**Files:**
- Modify: `tests/DigitalBrain.E2E.Tests/McpSurfaceTests.cs`
- Modify: `src/Kernel/DigitalBrain.Mcp/Program.cs`
- Modify: `src/Kernel/DigitalBrain.Mcp/McpSurface.cs`
- Modify: `src/Kernel/DigitalBrain.Mcp/ChatTools.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/TimeTools.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/IntrospectionTools.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatMessageResult.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatButtonOfferResult.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatChartOfferResult.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatChartPointResult.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatTranscriptPage.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/ChatTranscriptTurn.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/JournaledSynapse.cs`
- Delete: `src/Kernel/DigitalBrain.Mcp/NeuronJournalPage.cs`

1. Change the E2E tool-list test to assert the exact singleton `{ send_chat_message }` before changing production code.
2. Register only `ChatTools` and expose only the MCP path plus the `send_chat_message` name.
3. Make `SendChatMessageAsync` return the assistant text directly. Remove the button-activation tool and all structured result/offer projection.
4. Build the full solution and start Aspire. Use the MCP streamable-HTTP endpoint to list tools and send a chat message; require one tool and non-empty assistant text.
5. Commit: `refactor: reduce mcp to chat only`.

## Task 2: Remove Core capability and owner filters

**Files:**
- Modify: `src/Kernel/DigitalBrain.Core/Hosting/DigitalBrainRuntime.cs`
- Delete: `src/Kernel/DigitalBrain.Core/Filters/IncomingReificationFilter.cs`
- Delete: `src/Kernel/DigitalBrain.Core/Filters/OutgoingReificationFilter.cs`
- Delete: `src/Kernel/DigitalBrain.Core/Filters/OwnerBoundCallFilter.cs`

1. First add/adjust a test that starts the retained chat path without filter registration and asserts MCP `send_chat_message` and Kernel `/health` are reachable.
2. Remove the three filter registrations. Delete the three filter implementations as one atomic change.
3. Build and run the retained chat route through Aspire/MCP. If a direct call still references capability correlation, trace that use from `Chat` to the Client and delete or flatten the minimal remaining support code in the next task rather than restoring filters.
4. Commit: `refactor: remove capability call filters`.

## Task 3: Remove the unneeded support graph and global imports

**Files:**
- Delete, once unreferenced: `src/Kernel/DigitalBrain.Core/Filters/IOwnerBoundGrain.cs`, `src/Kernel/DigitalBrain.Core/GrainCallerContext.cs`, `src/Kernel/DigitalBrain.Core/ReminderSourceAllowlist.cs`, `src/Kernel/DigitalBrain.Abstractions/Neurons/ClientEntryPointAttribute.cs`.
- Delete or replace only after consumers are removed: capability request/outcome classes under `src/Kernel/DigitalBrain.Core/Capabilities/**`, capability event records under `src/Kernel/DigitalBrain.Abstractions/Capabilities/**`, and identity wrappers not needed by the singleton owner/chat route.
- Remove: every `GlobalUsings.Abstractions.cs` from retained projects, adding precise file-local `using` directives first.

1. For each candidate, use CodeGraph callers plus `rg` to prove no retained caller. Remove the candidate and build.
2. Replace each global import file with explicit imports in only the files that still need the namespace; never add a new global using as a substitute.
3. Keep only the identity data required to address the one retained owner and chat instances; remove multi-principal/partition semantics only after MCP/Flutter have moved to the singleton address.
4. Commit each independently verifiable reduction.

## Task 4: Final verification

1. Run `dotnet build DigitalBrain.slnx --no-restore`.
2. Start with `aspire start --non-interactive --format json`; wait for Kernel, MCP, and Flutter.
3. Initialize MCP, verify the exact tool set, and invoke `send_chat_message` against Gemma4.
4. Do not use Computer Use unless the user explicitly approves it again.
5. Leave user-owned `docs/research/` untouched.
