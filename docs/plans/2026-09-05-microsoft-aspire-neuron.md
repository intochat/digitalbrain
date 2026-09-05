# Microsoft-module Aspire neuron

Status: implemented and verified. Uses the existing AgentRequest/AgentReply contract and MCP-discovered tools throughout.

Build Aspire as a real specialist neuron in the Microsoft module. Ino delegates to it through the inherited `AgentRequest -> AgentReply` contract for every question, including routine status. Aspire's model selects and invokes tools discovered from its MCP server. The server owns tool names, descriptions, input schemas and result content. DigitalBrain supplies shared connection, invocation, policy and observation infrastructure without translating each MCP operation into a C# signal or provider-specific result model.

## Options

| Option | Benefit | Tradeoff |
| --- | --- | --- |
| Add Aspire MCP functions directly to Ino | Smallest implementation; one model handles everything | Repeats the current Gmail/Salesforce connector arrangement. No actual specialist neuron or delegated relationship. |
| Microsoft-module `Aspire : Agent, IAspire` using inherited AgentRequest and MCP-discovered tools | Domain ownership, observable delegation and an integration that follows the server's tool catalog | Requires a shared source-bound in-silo request/reply seam and MCP session management. Status also uses a specialist model turn. Selected direction. |
| Fully asynchronous infrastructure operator | Durable long-running investigations and operations that survive restarting the brain | Introduces operation scheduling, recovery and another execution boundary before the first status check needs them. Reserve for later restart/deploy work. |

The second option becomes the pattern for future Gmail/Salesforce agent facades without requiring their existing OAuth and write-confirmation implementations to be replaced now.

## Starting point before implementation

- `IGmail` and `ISalesforce` are connector service interfaces, not `IAgent` or `INeuron`. Their modules contribute functions to Ino through `IAgentToolSource`.
- `ChatTurnWorker` calls Ino through `IAgentKernel.AskStreaming`. Calling `IDigitalBrain.RequestAsync` from inside an active agent can deadlock the serialized owner-root send path.
- A direct `IAgentKernel.Ask` call also bypasses signal journals and source-owned Learned synapses. It cannot by itself provide the requested graph relationship.
- The graph projects real synapses and bounded journals. Shared Ino's edges to private targets are filtered by the current principal. A global `aspire:aspire` instance would not fit that rule.
- The shared SDK MCP client currently assumes Streamable HTTP and bearer credentials. Aspire's installed CLI is 13.5.3 and exposes `aspire agent mcp` over STDIO. It needs a process transport, not a Gmail OAuth configuration.
- Lumen already has an Aspire icon mapping, but the current mark is locally drawn. Use the official SVG asset for this implementation.

Code evidence: `src/Modules/Google/Contracts/IGmail.cs`, `src/Modules/Salesforce/Contracts/ISalesforce.cs`, `src/Modules/AI/AI/Assistant.cs`, `src/Modules/AI/AI/Agent.cs`, `src/Kernel/DigitalBrain/Neuron/SignalSender.cs`, `src/Kernel/DigitalBrain.Silo/BrainGraphProjection.cs`, `src/Kernel/DigitalBrain.Sdk/Mcp/McpToolClient.cs`.

## Module and contract shape

Proposed projects:

```text
src/Modules/Microsoft/
  Contracts/       DigitalBrain.Modules.Microsoft.Contracts
  Microsoft/       DigitalBrain.Modules.Microsoft
  Aspire.Hosting/  DigitalBrain.Modules.Microsoft.Aspire.Hosting
```

The Contracts assembly owns the thin `IAspire : IAgent` identity. It does not declare status requests, status DTOs, log/trace DTOs or methods corresponding to MCP tools. The implementation assembly owns `MicrosoftModule`, `Aspire`, its instructions and connection registration. Shared AI/SDK infrastructure handles MCP tool discovery and invocation. The hosting project supplies explicit connection configuration. This is separate from the existing `src/Aspire` hosting infrastructure: one configures the application, the other gives the assistant a capability to inspect it.

Use `DigitalBrain.Microsoft` as the public namespace, matching existing `DigitalBrain.Google` and `DigitalBrain.Salesforce` conventions. The assembly names retain `DigitalBrain.Modules.Microsoft.Contracts` and `DigitalBrain.Modules.Microsoft`. If the full namespace proposed in the discussion is preferred, make that an explicit repository-wide naming decision rather than mixing conventions in one module.

Conceptual contract, not compiled code:

```csharp
public interface IAspire : IAgent;

// Inherits IHandle<AgentRequest> and Agent's existing handler/reply behavior.
public sealed class Aspire : Agent, IAspire;
```

One neuron instance represents one authorized application connection for one principal. Its stable name includes the verified principal partition and a configured alias, for example `<principal>.digitalbrain-local`. Its label is **Aspire · DigitalBrain**; its module is **Microsoft**. Two chats belonging to the same principal may reuse that instance. Different principals do not share its private journals or mutable selected-AppHost state.

`Agent` already implements `HandleAsync(AgentRequest)`, the model/tool loop and `ReplyAsync(AgentReply)`. Aspire reuses that implementation. Its specialization consists of purpose/instructions, model selection if desired, and authorized MCP connection(s). Instructions require fresh observations before status claims, distinguish Running from Healthy, and disclose missing or truncated evidence; there is no hand-written resource-status parser.

All status and investigation requests therefore incur the specialist model/tool loop. This is an accepted tradeoff for keeping the integration flexible. A strict deterministic health monitor is a different requirement and must not be implied by natural-language agent output.

## Ino routing and the user experience

The Microsoft module registers the specialist's identity, purpose, authorized target resolver and presentation metadata. A shared delegation-tool factory exposes `ask_aspire(request)` to Ino from that registration. This is an instance of the common AgentRequest adapter, not a hand-written Aspire operation. The same factory can expose Gmail/Salesforce specialists later. Only Aspire receives its detailed MCP functions. The AI module does not reference the Microsoft implementation, and no separate runtime agent registry or discovery service is introduced.

For “Ino, get Aspire status”:

1. Ino chooses Aspire from its registered purpose and resolves the configured default application. With several connections and no established context, present their display names for selection. The model cannot supply an arbitrary endpoint or working directory.
2. An actual directed `AgentRequest("Check the current status of this application...")` delivery goes from Ino to the principal's Aspire neuron. Record delegation start before waiting so the first call is visible immediately.
3. The inherited Agent handler prepares the discovered MCP tools for the model. Aspire selects a suitable tool, normally `list_resources` for this question, using the server's current descriptions and schemas. The MCP SDK performs the invocation and returns the protocol result to the model.
4. Aspire returns its grounded `AgentReply`; Ino presents it to the user. Observation time and transport/tool outcome come from shared instrumentation. Resource conclusions come from the specialist's reading of the returned evidence and must disclose uncertainty.
5. Successful handled delivery creates/reinforces the existing Learned synapse. The in-flight visual must remain distinguishable from an established subscription.

For “why is the kernel failing?”, the exact same request/reply path applies. Aspire can make several bounded resource/log/trace calls before answering. No additional domain signal, method or tool-schema mapping is added for this question.

The graph shows Ino delegating, Aspire invoking the actual MCP tool, then completion/failure. The Aspire node opens connection state, discovered tool names/descriptions, last activity and the latest reply. A generic inspector can render bounded, screened text/JSON content from MCP rather than requiring Aspire-specific resource cards. Its edge inspector opens the AgentRequest exchange, timing and outcome, plus the actual Learned/Bound state. Activity animation follows observed events and respects reduced motion. Use the official brand icon unchanged and animate its surrounding activity ring.

Do not automatically make every MCP tool or Aspire process resource a neuron. They are capabilities and returned evidence of the Aspire neuron until DigitalBrain actually implements and addresses corresponding neurons.

## Shared MCP support in Agent

Extend Agent's tool preparation with an asynchronous MCP tool source: connect/lease a session, discover tools, apply the authorized tool policy, and supply the resulting `McpClientTool` instances to the existing model loop. The C# MCP SDK already makes `McpClientTool` an `AIFunction`; there is no need to generate per-tool C# methods or copy JSON schemas into DigitalBrain contracts.

Keep the distinction between module-contributed delegation tools for Ino and an individual specialist's own MCP tools. Aspire must not inherit all of Ino's Gmail, Salesforce and behavior tools merely because they are registered in DI.

The shared wrapper handles observation and policy while forwarding the server-provided name, description, input and available output schemas, and the MCP result envelope/content. It does not coerce results into Aspire-specific types. Where content must be bounded or screened, record that fact; do not silently rewrite the apparent source evidence. Model-visible provider/protocol incompatibilities remain explicit.

Cache catalog metadata for the session, refresh on reconnect and supported tool-list change notifications, and give each model turn a consistent authorized tool snapshot. Cached metadata may outlive a transport; callable tools must bind to the current valid session. New tool schemas flow from discovery without app recompilation. Newly discovered capabilities still obey the connection's authorization policy; discovery does not grant permission automatically.

Only standard MCP/protocol boundaries and generic agent lifecycle need shared types. Status, resources, traces and individual tool arguments/results stay owned by MCP. Aspire contributes connection configuration and purpose; additional MCP-backed specialists reuse this same path.

## The necessary request/reply seam

This is a bounded extension of existing delivery, not a new router, graph store or capability-discovery service.

- Evolve `IAgentToolSource.ToolsFor(owner)` to accept a transient `AgentToolContext` containing Owner and a source-bound request capability. Existing sources use Owner as before. Agent constructs a fresh context for each model turn, bound to its own activation; generated delegation tools close over that turn's request capability. Actor identity, restrictions and deadline remain turn-scoped. Singleton sources and cached tool functions must not retain a previous turn's context or cancellation state.
- Keep executable delegates out of the serializable Product `AgentTurnContext` / Orleans RequestContext. Preserve verified actor, chat/command identity and authorized tool restrictions across delegation. A restricted continuation cannot acquire a broader downstream tool grant merely by calling `ask_aspire`.
- The source-bound helper uses the caller neuron's `SendAsync` directly. It must not re-enter `IDigitalBrain` or make a request to its own busy grain proxy.
- Read the target outgoing journal cursor before sending. After handling, find the response whose CausationId equals this delivery's SignalId, matching target, cursor and expected response type. CorrelationId alone is insufficient when one chat turn makes multiple delegations.
- Observe the target's durable outgoing reply, not delivery back into the busy caller. Existing `ReplyAsync` records the reply before detached delivery to the caller.
- Thread cancellation/deadlines through `Neuron.SendAsync`, `SignalSender`, remote Deliver, agent/model and MCP operations. A timeout must stop source-state continuation before reinforcement. Merely placing `WaitAsync` around the whole existing sender can leave a continuation modifying neuron state after its turn ended.
- Distinguish timeout/unknown outcome from confirmed cancellation. Handle unavailable, ignored delivery, missing or compacted reply, and MCP errors explicitly. Late replies cannot satisfy a later request. Prevent self-delegation and delegation cycles; Aspire has no reverse `ask_ino` tool in this slice.

Add safe generic delegation/tool-start/completion/failure journal observations shared by every agent. They contain the target, request identity, tool name, duration, observation time, protocol error flag and truncation state. These describe runtime activity, not the business schema of a tool's response. Extend the graph's positive projection to discover an actual in-flight participant and to summarize these observations. Do not create a fake permanent edge to show an animation. Raw arguments, logs, credential-bearing URLs and arbitrary output do not enter graph previews. A separately opened result inspector uses authorized, bounded and screened content.

## Aspire MCP connection

The Microsoft module configures a shared MCP session facility to own `McpClient` plus the `aspire agent mcp --non-interactive` child process. Configure the canonical local AppHost project `D:/digitalbrain/src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` and working directory explicitly. Validate detected AppHost identity; do not select whichever application happens to be first. Keep selection and calls isolated per authorized principal/connection, with bounded capacity, idle disposal and reconnect-on-next-read behavior. Target binding is connection setup, not a mapping of each operational tool into a domain method.

Discover the available MCP catalog and use its schemas directly. Initially authorize the resource/log/trace tools needed for the read-only slice. A configured tool policy can select allowed tools by identity without maintaining their argument or result schemas. AppHost discovery/selection belongs to connection setup. Do not expose arbitrary command execution or mutable AppHost selection to the model. Honor MCP `IsError`, structured content and truncation; preserve cancellation and normalize transport failures. Treat telemetry text as untrusted input.

The precise installed `list_resources` response schema does not become a DigitalBrain contract. Validate that actual MCP content reaches the model and generic inspector correctly, including text-only results, structured content, tool errors and truncation. Do not implement a resource-status parser or recover missing fields through substring guesses.

Put the reusable connection/discovery/invocation mechanism in the existing SDK/AI boundaries. Add the required STDIO transport without forcing it through the current HTTP/bearer assumptions or replacing the existing Gmail/Salesforce credential and write workflows in this slice. Each new MCP integration supplies connection/authentication policy and agent purpose; it should not need its own tool wrapper class for every remote operation.

For local development this process can live alongside the specialist in the kernel host. A remote dashboard can later be reached through the CLI's documented dashboard connection mode; it is not evidence of a raw dashboard MCP HTTP endpoint. Remote production support needs an explicit deployment/credential design.

The IAW reference supplies the specialist concept and MCP-tool discovery pattern. Do not copy its stale AppHost path discovery, older MCP command, string-based health checks, swallowed connection errors, automatic activation-time monitor, hardcoded deployment path or stop-then-start of its own host.

Official references, checked 2026-09-05:

- [Aspire MCP server](https://aspire.dev/get-started/aspire-mcp-server/): current STDIO transport and resource/log/trace tool surface.
- [aspire agent mcp](https://aspire.dev/reference/cli/commands/aspire-agent-mcp/): supported CLI command and dashboard connection options. Also verified against local `aspire --version` and `aspire agent mcp --help`.
- [Official Aspire brand assets](https://microsoft.github.io/aspire-brand/): supplied SVG icon and brand guidance.
- [MCP C# SDK tool integration](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html): discovered McpClientTool instances can be passed directly to IChatClient as AIFunctions.
- [McpClientTool API](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Client.McpClientTool.html): server metadata/schema and generic invocation result handling.
- Local reference: `D:/IAW/src/Agents/Infrastructure/IAspire.cs` and `AspireAgent.cs`.

## Subscriptions and subsequent capabilities

A one-off request needs no Bound subscription. Normal handled delivery creates/reinforces a Learned route. Removing a subscription does not revoke the capability to make a new explicit request.

A later “tell me when application health changes” feature uses an explicit behavior that sends an ordinary AgentRequest on its configured trigger and handles the AgentReply. It does not require an Aspire-specific health-change signal. Any interpretation or comparison of model output is a behavior decision, with its uncertainty explicit, rather than a claim of deterministic typed health detection.

Subscribe/Unsubscribe continue to operate on existing signal types that receivers actually handle. Directed AgentReply is a reply to one request; it is not automatically broadcast to subscribers. If a monitoring behavior needs fan-out, it explicitly publishes an existing suitable signal (for example Note to capable recipients) along its source-owned subscriptions. Unsubscribe removes that Bound connection; no implicit fallback broadcast occurs. The monitor's schedule and subscriptions have separately managed lifetimes. General monitoring/fan-out is a later design slice, not a reason to invent provider-specific contracts here.

MCP reads do not automatically supply a push feed. A separately managed behavior/time trigger must perform checks. Subscribing alone must not silently create an activation-time polling loop. Native MCP progress or tool-list notifications, when supported, are protocol features and do not change DigitalBrain's subscription semantics.

Restart/deploy comes after this read-only slice. Managing the kernel/AppHost hosting the agent requires an independent operator and durable verification persisted before the restart; otherwise the caller can kill itself before reporting or completing the operation.

## Implementation sequence and acceptance

1. Add the thin Microsoft IAspire contract, module registration and explicit local connection configuration. Implement shared asynchronous MCP tool discovery/preparation and verify server-provided schemas/results reach the model unchanged except disclosed generic policy/screening/budget handling.
2. Add and test the source-bound in-silo request/reply seam, cancellation behavior and caller/response identity. This is the main correctness dependency.
3. Add Aspire's instructions and MCP connection binding while reusing Agent's existing handler. Generate Ino's one delegation function from the specialist registration. Make Microsoft contracts available to scripts for `IAspire` plus existing `AgentRequest` calls.
4. Add journal activity projection, first-call participant discovery, official icon and inspector content to Lumen. Preserve current graph principal filtering and chat acceptance/recovery.
5. Verify the complete Flutter scenario against the running application, then failures and concurrent/repeated requests.

Required evidence before calling the slice complete:

- Asking for Aspire status in Flutter delivers AgentRequest to the configured application's Aspire neuron, invokes a real discovered MCP tool and returns a grounded AgentReply. An investigation uses the same path.
- A fake MCP server can add a permitted tool or change its input schema; after catalog refresh the model can use it without adding DigitalBrain signals, DTOs or per-tool wrapper code. New permission grants remain explicit.
- MCP text/structured results, errors and truncation remain distinguishable through the shared wrapper. Ino never receives the full Aspire catalog.
- First-request graph activity is visible before completion, with no fabricated subscription. A handled request produces the correct source-owned Learned edge.
- A real-silo test demonstrates nested delivery completes without owner-root or caller-reply deadlock; causation matches multiple requests correctly.
- Deadlines, cancellation, CLI/MCP disconnect and tool errors produce bounded, truthful outcomes, including late responses.
- Different principals and multiple configured targets cannot share selection state or view each other's activity. Tool restrictions survive delegation.
- No raw sensitive telemetry appears in graph previews. Existing chat recovery and subscription semantics still pass their focused regressions.

Health subscriptions and external restart/deploy operation recovery have separate acceptance tests when those later features are implemented.

## Implementation evidence — 2026-09-05

- Microsoft contracts, specialist, connection, hosting registration and scripting references are implemented. Only the shared AgentRequest/AgentReply contract is used for delegation. Ino receives `ask_aspire`; the specialist receives the permitted native MCP catalog.
- The initial live failure was before the specialist model call: `ask_aspire` → IAspire delivery → MCP preparation failed after about 2.9 seconds. The installed Aspire CLI lazily discovers AppHosts. `list_apphosts` before `select_apphost` fixes the binding; regression tests cover ordering, protocol errors and owner/target refusal.
- The first healthy-resource count could not be answered because a 63,941-byte inventory exceeded the original 24,000-byte result budget. `list_resources` publishes an empty argument schema with no filters. The response and whole-content screening caps are now 128 KiB, with explicit omission above the cap. JSON embedded after MCP prose is structurally redacted, including credential-bearing URLs, without mapping resource data into C# DTOs.
- The compact chat had an independent reproduced defect: an old send that failed before acceptance was appended after a newer durable response and hid it. Compact display now chooses by message timestamp and retains feedback for active requests. Exact command-based recovery remains unchanged.
- AI content was hardcoded off despite an exposed hosting setting. The MEAI pipeline now honors the configured opt-in. Local Development run mode enables it; normal pipeline and publish defaults remain off. Agent spans carry agent/conversation/command identity and the MEAI parent-span content flag. Model and tool content are recorded by MEAI once. See Microsoft Learn and Aspire configuration links in `docs/GETTING_STARTED.md`.
- Live chat through Flutter's HTTP send endpoint returned the resource count, with a real Learned Ino→Aspire edge and nontruncated MCP evidence. Trace `435f5ac0c7169ce5b2579cb0fc9bbb14` contained two agent spans, four model spans with input/output content, delegation arguments/results, and the complete resource-tool result. Dashboard traces are in-memory and restart clears them; sanitized metadata is saved under ignored `artifacts/aspire-investigation`.
- Validation: host build clean; 33 focused Simulation tests, 19 substrate tests, 8 scripting compiler tests, 2 telemetry hosting tests, and 13 graph/HTTP/chat-stream backend tests passed. Flutter: 85 graph/core/kit tests plus 18 compact/surface/workspace tests passed; affected Dart analysis is clean. Native backend requests and event recovery were checked over HTTP; the latest compact UI behavior was verified in widget tests.

Run the full HTTP/AppHost fixture with the main AppHost stopped: both use port 5080. The first overlapping validation run was cancelled, then all 13 backend checks passed with the fixture running alone.

Final live retry of the exact question, “How Many aspire resources are healthy”, completed in 9.7 seconds and returned 17 Healthy resources. Trace `d5632ede016db77e8f5d79631ae891d1` remains in the running dashboard with model inputs/outputs and tool arguments/results. The redacted resource body parses as valid native JSON (21 total entries), and `list_resources` completed without truncation or protocol error.
