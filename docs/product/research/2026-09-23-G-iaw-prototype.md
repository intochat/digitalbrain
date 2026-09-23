# G-iaw-prototype

## Summary
IAW (E:\projects\IAW) is the owner's earlier Orleans + Aspire + Telegram prototype. Its last commit is 2026-04-20, it has about 339 C# files and 411 xUnit tests. It already tried three of the ideas IntoChat now wants: a semantic, renderer-agnostic UI protocol (UIPart), a searchable catalog of every agent with embeddings, and a per-user approval agent with scopes for "allow once", "allow in this conversation" and "always allow". All three exist in code, but each is only partly implemented. The UI protocol defines 8 part types. Only options, suggestions, media and forward-message are ever produced and rendered, and Telegram is the only renderer. Most UI is inferred after the fact from the LLM's text, using regex first and an LLM reformatter as a fallback. Button clicks go back to the LLM as plain text ("I choose: X"), not to typed handlers. The widget engine for wizard, menu, paginator and form is unit-tested, but no production code ever calls it.

The "vector DB of agents" is an in-memory Orleans grain, not Qdrant (the spec said Qdrant). It indexes the display name, description, capabilities and routing examples declared as static members on each agent's C# interface. It ranks with 0.6 × cosine similarity plus 0.4 × keyword score and puts the top 5 agents into every Thread prompt. Embeddings are built once at silo startup. When the grain is reactivated after the default 15-minute idle collection, it rebuilds its records without embeddings, so search quietly becomes keyword-only. It also becomes keyword-only when no embedding provider is configured. Individual tools are not indexed; only whole agents are.

For the marketplace OS, the most reusable ideas are:
- the interface-as-manifest pattern: static metadata plus methods with [Description] that become AI tools;
- the approval scopes and natural-language policies in ApproverAgent;
- the usage-capturing IChatClient decorator, as a seed for Compute metering;
- automatic memory with provenance: IawMemoryProvider, using the Microsoft Agent Framework context-provider pipeline;
- the TestCluster test harness and the architecture-guard tests.

IAW also shows what to avoid:
- **Volatile "durable" state.** Every agent is a journaled DurableGrain, but the production silo uses VolatileStateMachineStorageProvider plus memory storage, streams and reminders. The README says state survives restarts; it doesn't.
- **An LLM judging every tool call.** Approval is decided by an LLM and gated only for grain keys that start with a Telegram user ID. Orchestrated `task-*` agents and typed grain calls skip it entirely.
- **Raw chat stored in Qdrant.** Every message is embedded as-is, including any passwords users type.
- **Untyped state.** State is a `StateEntry(string, object)` bag, and user preferences are a string-to-string dictionary.
- **Duplicate form types.** There are two conflicting FormFieldType enums. Only one of them has Date, and the one the widget engine actually uses does not. This is the same class of bug as IntoChat's `"date"` field-kind failure: DigitalBrain's TextFieldNeuron only accepts text, number, password and suggest.

IAW has no concept at all of billing, credits, pricing, marketplace, manifests, tenants or sandboxing. The only metering is OpenTelemetry token metrics.

Most useful for highlevel.md: IAW already worked out the vocabulary (UIPart, AgentRecord, approval scopes, memory provider, tiers). Its failures came from three things. Semantics were left to the LLM where deterministic contracts should have been (UI inference, security judgment, callback handling). Durability and indexing were never made real (volatile storage, in-memory registry). Docs described intent rather than code: the README claims 65+ agents where there are about 17 working agents plus 13 model wrappers, and the website still documents 5 memory agents that were deleted in commit a04ea9e. IntoChat should take the contracts and drop those shortcuts.

## Findings

### G1 (fact, high) [VERDICT: partially] IAW UI model: flat, renderer-agnostic UIPart records, but Telegram is the only renderer and half the parts are never used
Core defines one semantic UI vocabulary with no Telegram dependency: `abstract record UIPart` with TextPart(Content, TextStyle), OptionsPart(Prompt, Options, CallbackId, AllowMultiple), CardPart(Title, Fields, ImageUrl), MediaPart(Url, FileName, MimeType, Caption), ProgressPart(Message, Percent), FormPart(CallbackId, Prompt, Fields), SuggestionPart(CallbackId, Actions) and ForwardMessageHint. A response is `AgentResponse(List<UIPart>)` or `RichOutput(FormattedText, Parts)`. The model has no layout, nesting, component identity, bound state or update-in-place. It is a flat list of decorations attached to one text message.

Mapping to Telegram (ResponseStreamer):
- Text becomes a Telegram HTML message, streamed by editing the message every 1.5 s and split every 4000 characters.
- OptionsPart and SuggestionPart each become one InlineKeyboardMarkup row, with callback_data `opt:{callbackId}:{value}`.
- MediaPart becomes a sendPhoto or sendDocument call.
- ForwardMessageHint becomes forwardMessage.

CardPart, FormPart, ProgressPart and TextStyle are never produced or rendered anywhere (a grep finds only their definitions). No Telegram Mini App (web_app) is used; there are no WebApp references in src. Telegram limits are hard-coded into the generators: 40-character labels, 8 options, blob.core.windows.net URLs, and callback_data is limited to 64 bytes by the Bot API. DevUI and MCP receive only text. The design spec's renderer table (Telegram vs MCP) was never built beyond Telegram.

Adoption: keep the idea of a small, closed, semantic part vocabulary that is independent of the client. IntoChat's Flutter component neurons are already richer, since they are stateful and composable. Treat IAW's list as the minimum set a secondary renderer such as Telegram must support, and define a documented fallback for each part type.
Evidence: E:\projects\IAW\src\Core\UI\UIPart.cs:4-76; E:\projects\IAW\src\Core\UI\AgentResponse.cs:4; E:\projects\IAW\src\Core\UI\RichOutput.cs:4; E:\projects\IAW\src\Telegram\Services\ResponseStreamer.cs:142-183; E:\projects\IAW\src\Telegram\Services\ResponseStreamer.cs:19-20,43-66; E:\projects\IAW\docs\superpowers\specs\2026-03-19-agent-registry-orchestration-redesign.md:377-434 (renderer table); https://core.telegram.org/bots/api#inlinekeyboardbutton (callback_data 1-64 bytes; web_app = Mini Apps)

> CORRECTION: The core is right. Core.UI has a flat record vocabulary (E:\projects\IAW\src\Core\UI\UIPart.cs:1-74; the file is 74 lines, not 76). The Telegram mapping is as described (ResponseStreamer.cs:19-20,43-66,142-183). CardPart, FormPart, ProgressPart and TextStyle are referenced only in UIPart.cs and test/Core.Tests/UI/UIPartTests.cs. There is no web_app use, and DevUI and MCP get text only (src/DevUI/OrleansAgentChatClient.cs:59, src/MCP/Tools/AgentTools.cs:75-82).

Corrections and omissions:
(1) The vocabulary is not fully renderer-agnostic. ForwardMessageHint(TelegramMsgId) is a Telegram-specific part living in Core.UI (src/Core/UI/ForwardMessageHint.cs:4), and MemoryHit/IawMemoryProvider carry SourceTelegramMsgId.
(2) The LLM does generate UI, which the finding leaves out. TelegramFormatter first runs a deterministic RichContentParser, which turns regex-numbered lists into options (at most 8, labels cut at 40 characters) and blob URLs into media. For responses of 300+ characters it falls back to an LLM agent, ITelegramUI/TelegramUIAgent, whose prompt asks for JSON {formattedText, parts:[options|suggestions|media]} (src/Telegram/Formatting/TelegramFormatter.cs:11-44; src/Agents/Orchestration/ITelegramUI.cs:16-61; TelegramUIAgent.cs:30-133). Agents can also call the ProposeOptions tool (src/Core/Agents/Agent.Tools.cs:38-56).
(3) Parts do not compose across agents. The Thread flattens a sub-agent's AgentResponse to TextPart text plus MediaPart deliveries (ThreadAgent.cs:186-188), so a sub-agent's OptionsPart/ProposeOptions hints are dropped.
(4) 'No update-in-place' is overstated. Streaming edits the message in place, and CallbackRouter edits the message text and keyboard on callbacks (src/Telegram/Services/CallbackRouter.cs:40-55).
(5) The renderer ignores OptionsPart.AllowMultiple and Option.Description. Media can also go out as sendMediaGroup (TelegramFileService.cs:60-100).
(6) A second, nested model (MenuNode with Children, WizardStep, ButtonRow) exists in Core.Contracts.UI (src/Core/Contracts/UI/Button.cs:4-22), so IAW is not purely flat, though that model is dead in production.

### G2 (pattern, high) How the LLM produces UI: explicit tool first, regex inference second, LLM reformatter last
UI is produced in three layers, merged after the text stream finishes:

1. **Explicit tool.** Every agent exposes the protected `ProposeOptions(prompt, string[] options)` AI tool. It buffers an OptionsPart as a pending UI hint, trimmed to 8 options and 40 characters, and returns a text confirmation to the LLM. The Thread instructions forbid inline 'A)/1.' choices. Telegram drains the hints through `IThread.GetPendingUIHints` after streaming.
2. **Deterministic parse.** `RichContentParser` is described as handling '95%+' of responses. It uses regex to find numbered or lettered lists (becoming options), 'would you like / you can also' plus bullets (becoming suggestions), and Azure blob URLs (becoming media).
3. **LLM fallback.** `TelegramUIAgent` runs on the Fast tier with no history. It is only called for text of 300+ characters that looks list-like. It rewrites the raw text into JSON `{formattedText (Telegram HTML), parts:[options|suggestions|media]}`.

`MergeHintsIntoParts` lets explicit hints override any options inferred by the parser.

**Strengths:** the LLM never writes raw widget markup, the cheap path runs first, and a deliberate tool call beats inference.

**Weaknesses:**
- UI is decoration inferred from prose, so it produces false positives on lists and costs an extra LLM call.
- The UI decision lives in the client process (TelegramFormatter), not in the brain.
- The parts cannot be addressed or updated later.

Adoption for IntoChat: keep 'explicit tool first, hints override inference'. Drop the post-hoc regex/LLM inference as a way to create UI; at most use it to suggest follow-up actions.
Evidence: E:\projects\IAW\src\Core\Agents\Agent.Tools.cs:38-56; E:\projects\IAW\src\Agents\Orchestration\IThread.cs:31-36,48; E:\projects\IAW\src\Telegram\Formatting\RichContentParser.cs:10-33,146-150; E:\projects\IAW\src\Telegram\Formatting\TelegramFormatter.cs:9-42; E:\projects\IAW\src\Agents\Orchestration\ITelegramUI.cs:16-61; E:\projects\IAW\src\Agents\Orchestration\TelegramUIAgent.cs:14-57; E:\projects\IAW\src\Telegram\Services\ResponseStreamer.cs:84-94,118-134; E:\projects\IAW\docs\superpowers\specs\2026-03-21-environment-aware-rich-responses-design.md (Hybrid AI Tool + Smart Fallback)

### G3 (pattern, high) Events and callbacks: string-encoded Telegram callbacks, a widget state-machine grain, and clicks sent back to the LLM as text
`UISession` is a DurableGrain keyed by Telegram user ID. It holds IDurableDictionary state machines for wizards, paginators, menus, forms and pending option sets. A reminder every 5 minutes expires them after 10 to 60 minutes. Callback data is a string `type:id:action` (wz, pg, mn, fm, opt, plus ap for approvals and cmd for commands). `HandleCallback` returns `CallbackResult(NewText, Action, Toast, Buttons)`, and `CallbackRouter` applies it by editing the Telegram message.

When the user clicks an option or suggestion, the selection becomes a new natural-language user turn, `"Re: '<prompt>' -- I choose: <label>"`, and is streamed to the Thread LLM again. The event is never bound to a typed handler.

StartWizard, StartPaginator, StartMenu and StartForm have unit tests (test/Core.Tests/UI/*) but zero production callers. Only RegisterOptions is used, from ResponseStreamer.

The spec's unified model (spec section 6) was: every agent implements `HandleCallback(callbackId, value)`, and the Thread keeps a durable table mapping callbackId to the grain that should receive it. That exists only as code that is never called: `Agent.HandleCallback` returns an empty response, and `ThreadAgent.RegisterCallback` is never called.

**Lesson:** events should be routed to typed operations on the neuron that emitted the UI, with state held there. Routing clicks through the LLM costs tokens, loses structure, and cannot carry typed values such as dates.
Evidence: E:\projects\IAW\src\Agents\UI\UISession.cs:8-133,291-363; E:\projects\IAW\src\Core\Contracts\IUISession.cs:5-17; E:\projects\IAW\src\Core\Contracts\UISessionDurableState.cs:6-19; E:\projects\IAW\src\Telegram\Services\CallbackRouter.cs:18-100 (line 94: "I choose: {selectedLabel}"); E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:358-389; E:\projects\IAW\src\Core\Agents\Agent.cs:444-451; grep: RegisterCallback/StartWizard/StartMenu/StartForm/StartPaginator have no callers outside UISession/tests

### G4 (risk, high) [VERDICT: partially] Two conflicting form-field type enums: the same root cause as IntoChat's rejected 'date' field
IAW has two unrelated form models:
- `Core.UI.FormField/FormFieldType { Text, SingleChoice, MultiChoice, Date, Number }`, which is what the LLM-facing protocol and spec describe;
- `Core.Contracts.UI.FormField/FormFieldType { SingleChoice, MultiChoice, FreeText }`, which is what the UISession widget engine actually runs.

Date therefore existed in the contract but could never be rendered or collected.

IntoChat's current failure is the same class of bug. DigitalBrain's TextFieldNeuron accepts only the string kinds text, number, password and suggest (`Kind` is a free string property), so an assistant-authored card asking for a 'date' kind was rejected only at deploy or runtime.

Recommendation for the typed SDK and UI kit:
- Define ONE closed, typed set of field and value kinds (Text, Number, Date, DateTime, Email, Secret/Password, Choice…), shared by the contract, validation, persistence and every renderer.
- Generate the LLM-facing tool schema from that set (an enum in the JSON schema), so the model cannot invent a kind.
- Treat a kind that a renderer doesn't support as a capability negotiation with a declared fallback (for example Date falls back to a validated text field with yyyy-MM-dd), not as a failed deployment.
Evidence: E:\projects\IAW\src\Core\UI\UIPart.cs:54-64; E:\projects\IAW\src\Core\Contracts\UI\Button.cs:25-32; E:\projects\IAW\src\Agents\UI\UISession.cs:291-319 (only FreeText/SingleChoice/MultiChoice handled); src/Modules/Google/Flutter/Flutter/TextField/TextFieldNeuron.cs:15; src/Modules/Google/Flutter/Contracts/TextField/ITextField.cs:20

> CORRECTION: The two enums exist as described: Core.UI.FormFieldType {Text, SingleChoice, MultiChoice, Date, Number} (UIPart.cs:64) and Core.Contracts.UI.FormFieldType {SingleChoice, MultiChoice, FreeText} (Button.cs:32).

Several claims are overstated:
(a) No LLM-facing prompt in src describes FormPart or Date. ITelegramUI only offers options, suggestions and media (ITelegramUI.cs:46-51). Only the spec does (docs/superpowers/specs/2026-03-19-agent-registry-orchestration-redesign.md:410-437).
(b) UISession does not 'actually run' in production. StartForm, StartWizard, StartMenu and StartPaginator are called only from test/Core.Tests/UI/FormTests.cs and similar tests. FormPart is never constructed. So in IAW the mismatch was latent dead code, not a failure users hit.
(c) Calling it the 'same root cause' is an analogy. IAW has two divergent closed enums. IntoChat has an open `string kind` contract (ITextField.cs:9, TextFieldState.Kind at ITextField.cs:20) with a whitelist hidden in the implementation (TextFieldNeuron.cs:15,21, which throws ArgumentOutOfRangeException). Grep finds the allowed kinds nowhere else in src, so neither the contract nor any authoring surface tells the LLM which kinds are valid.

The recommendation (one closed kind set, emitted as a JSON-schema enum, with capability fallback) is still sound and fits the IntoChat evidence better than the IAW evidence.

### G5 (fact, high) [VERDICT: partially] The agent 'vector DB' is an in-memory registry grain with hybrid scoring, not a Qdrant collection
**Declaring metadata.** Each agent's C# interface declares static virtual members on IAgent: AgentDisplayName, AgentDescription, AgentCapabilities (tags), AgentInstructions (the system prompt) and AgentRoutingExamples (example user utterances). Seven interfaces define routing examples.

**Discovery at startup.** At silo startup, the IStartupTask `AgentRegistrationStartupTask` reflects over all AppDomain assemblies for non-abstract `Agent` subclasses. It reads the leaf interface's static metadata and derives the namespace from the last segment of the .NET namespace (system, coding, orchestration, models, security…). It then embeds `"{DisplayName}: {Description} {capabilities} {examples}"` in one batch.

**Embedding model.** An Ollama model if one is declared (for example mxbai-embed-large, 1024 dimensions). Otherwise text-embedding-3-small (1536 dimensions) via GitHub Models or OpenAI. Otherwise `NoOpEmbeddingGenerator`, which returns zero vectors.

**Storage.** Records go into `AgentRegistryGrain("global")`, a plain Grain holding a `Dictionary<string, AgentRecord>`. AgentRecord carries Microsoft.Extensions.VectorData attributes (full-text-indexed Description, 1536-dimension cosine vector), but it is never written to any vector store, although the spec called for a Qdrant `agent-registry` collection with hybrid search.

**Search.** `HybridSearchAsync` scores 0.6 × cosine plus 0.4 × the fraction of query terms found in the concatenated text; with a zero vector it falls back to keyword-only.

**Consumers:**
- AgentRoutingContextProvider runs before every Thread turn. It takes the top 8, drops orchestration agents, and injects the top 5 as a system message `## Available agents for this request` with descriptions, capabilities and examples. The Thread then calls `SendToAgent(displayName)`.
- AgentSelectorAgent is used for Orchestrate. It calls keyword-only SearchAsync (top 15, excluding the 'models' namespace), then an LLM returns JSON with status Ready, NeedsClarification or CannotHandle, the selected agents, success criteria and a plan.
- CodeOrchestrator gets the full catalog through ToPromptStringAsync.

Only agents are indexed. Tools are attached per agent by reflection and are not searchable across the system.
Evidence: E:\projects\IAW\src\Core\Contracts\IAgent.cs:7-11; E:\projects\IAW\src\Core\Registry\AgentRegistrationStartupTask.cs:12-86; E:\projects\IAW\src\Core\Registry\AgentRecord.cs:1-43; E:\projects\IAW\src\Core\Registry\AgentRegistryGrain.cs:5-149; E:\projects\IAW\src\Core\Context\AgentRoutingContextProvider.cs:14-70; E:\projects\IAW\src\Agents\Orchestration\AgentSelectorAgent.cs:17-56; E:\projects\IAW\src\Agents\Orchestration\IAgentSelector.cs:16-33; E:\projects\IAW\src\Aspire.Client\LlmRegistration.cs:206-251; E:\projects\IAW\src\Agents\Infrastructure\IShell.cs:16-18; E:\projects\IAW\docs\superpowers\specs\2026-03-19-agent-registry-orchestration-redesign.md:173-303; https://learn.microsoft.com/dotnet/ai/vector-stores/vector-search (IKeywordHybridSearchable / IsFullTextIndexed)

> CORRECTION: Mostly accurate: the in-memory Dictionary in a plain Grain (AgentRegistryGrain.cs:6-8); the 0.6/0.4 hybrid score (:94-96); AgentRecord's VectorData attributes are never used with any vector store (grep finds no VectorStore/GetCollection usage, and 'agent-registry' is only the grain type name in IAWConstants.cs:26); the embedding fallback order (LlmRegistration.cs:206-251; note a GitHub Models key wins over an OpenAI key); and the three consumers.

Corrections:
(1) Only FIVE interfaces define AgentRoutingExamples (IAspire, IFileSystem, IShell, IPlaywright, IGitHub), not seven.
(2) The startup task does not resolve the 'leaf' interface. It takes the first non-generic IAgent-derived interface from GetInterfaces() (AgentRegistrationStartupTask.cs:62-63). Leaf resolution exists only in Agent.Tools.cs:126-136.
(3) AgentRoutingContextProvider excludes only IThread, IAgentSelector, ICodeOrchestrator and ITelegramUI (AgentRoutingContextProvider.cs:14,42-45). It does not exclude the 14 'models' LLM-wrapper agents, which therefore stay routable via SendToAgent.
(4) When no candidate scores above 0, it injects ALL non-orchestration agents into the prompt (:47-54).
(5) NoOpEmbeddingGenerator defaults to 384 dimensions (NoOpEmbeddingGenerator.cs:5; LlmRegistration.cs:245), not the 1536 declared on AgentRecord.

### G6 (risk, high) [VERDICT: confirmed] The agent index goes stale or degrades silently: embeddings are lost on reactivation, the no-op fallback is silent, and model changes aren't handled
Keeping the index in sync with code happens only through a full rebuild when the silo starts.

**Embeddings lost on reactivation.** `AgentRegistryGrain.OnActivateAsync` refills `_records` from reflection with NO embeddings whenever the grain activates. The grain has no persistence and no KeepAlive. Orleans' default CollectionAge is 15 minutes, so after 15 idle minutes (or a silo restart before the startup task runs) HybridSearch treats every record as a zero vector and silently becomes keyword-only. Keyword scoring also uses substring `Contains`, so 'git' matches 'digit'.

**Silent no-op fallback.** Without an embedding API key, NoOpEmbeddingGenerator produces zero vectors, and there is no health signal.

**Dimension mismatch.** Per-user memory and RAG Qdrant collections are created with the dimensions of the first embedding and never migrated. Switching embedding model (1536 to 1024) breaks upserts and searches, and those errors are only logged as warnings.

**No runtime install.** Nothing can register an agent or app at runtime (spec: 'Runtime: Read-only').

Recommendation for IntoChat's 'find any part of the system' catalog:
- Use a persistent Qdrant collection, or Microsoft.Extensions.VectorData with hybrid search.
- Make the point ID a stable hash of (module, neuron interface, operation, version).
- Re-embed only when the content hash changes.
- Put the embedding model ID and dimensions into the collection name or metadata.
- Delete orphaned points on sync.
- Expose an 'index degraded / keyword-only' health state in Aspire.
- Update the index on marketplace install/uninstall, not only at startup.
Evidence: E:\projects\IAW\src\Core\Registry\AgentRegistryGrain.cs:6-18,80-96,134-140; E:\projects\IAW\src\Core\Registry\AgentRegistrationStartupTask.cs:25-39; E:\projects\IAW\src\Core\AI\NoOpEmbeddingGenerator.cs:5; E:\projects\IAW\src\Core\Memory\IawMemoryProvider.cs:92-104; E:\projects\IAW\src\Agents.Host\Program.cs:9; https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/activation-collection (default Collection Age Limit 15 minutes)

### G7 (recommendation, medium) Adopt 'interface as manifest, methods as tools' for discovery, but add a declarative, versioned manifest
IAW's best reusable idea is that a capability's C# interface is its contract, its prompt and its catalog entry at the same time:
- static metadata (display name, description, capability tags, instructions, routing examples);
- typed methods, callable directly from code at zero tokens;
- every leaf-interface method with a simple return type becomes an AIFunction automatically, with [Description] engineered from a template: '{verb} {object}. {when to use}. Returns {shape}.'.

Architecture-guard tests enforce the conventions by reflection.

Gaps to close for a marketplace:
1. Compiled-in static metadata can't be installed, versioned or trusted. Generate a declarative manifest from it at build time: app ID, publisher, version, module and namespace, neuron interfaces, operations with JSON schemas, permissions, tariffs, UI parts used, and Gherkin examples.
2. Index at three levels (app, neuron, operation) plus behaviors and scenarios as separate vector points with payload filters. IAW only indexed agents, which is why the Thread LLM still needed the full tool list of the chosen agent.
3. Keep the two-stage flow: a cheap vector/keyword shortlist, then an LLM choice. Add clarification output (NeedsClarification with options) as a typed UI part rather than prose. IAW's ThreadAgent.FormatClarificationResponse flattens questions into text.
Evidence: E:\projects\IAW\src\Core\Contracts\IAgent.cs:5-52; E:\projects\IAW\src\Core\Agents\AgentGeneric.cs:6-12; E:\projects\IAW\src\Core\Agents\Agent.Tools.cs:94-124; E:\projects\IAW\docs\superpowers\specs\2026-03-23-agent-team-v2-design.md:203-241; E:\projects\IAW\test\Core.Tests\ArchitectureGuardV2Tests.cs; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:243-256; src/Modules/AI/AI/Agents/AgentTurnRunner.cs:15-18 (IntoChat's IAgentToolFactory seam, the likely attachment point)

### G8 (risk, high) [VERDICT: confirmed] Methods returning typed results are excluded from LLM tools, so each agent keeps a second, string-returning API
`DiscoverInterfaceTools` silently skips any interface method whose return type is not string, primitive, enum or an array of those (`IsToolSafeReturnType`). As a result, `IDotNet.BuildAsync -> Task<BuildRunResult>`, `IShell.ExecuteAsync -> Task<CommandResult>` and similar methods are invisible to the LLM. Agents then hand-write duplicate `DefineTools()` wrappers that call the typed methods and format a string (ShellAgent: RunShell, RunDotnet, RunPowerShell).

The outcome is two APIs per capability: typed for generated code, stringly-typed for the LLM. Descriptions drift between them, and the LLM never sees structured results. This is relevant to IntoChat's 'Show me all customers' flow, where the assistant has to validate SQL and table views through several string round-trips.

Recommendation: the SDK should expose typed records to the LLM as JSON (Microsoft.Extensions.AI supports structured return values) and compact large results into a summary plus a handle. That gives one surface, not two.
Evidence: E:\projects\IAW\src\Core\Agents\Agent.Tools.cs:110-113,170-183; E:\projects\IAW\src\Agents.CSharp\DotNet\IDotNet.cs:35-47; E:\projects\IAW\src\Agents\Infrastructure\IShell.cs:38-46; E:\projects\IAW\src\Agents\Infrastructure\ShellAgent.cs:30-60

### G9 (pattern, high) Consent: ApproverAgent with once/thread/user scopes and natural-language policies, fail-closed, but decided by an LLM on every call
**Mechanism:**
- Every agent's Microsoft Agent Framework AIAgent is built with `.Use(ToolApprovalMiddleware)`. Each LLM tool call sends `ToolAuthorizationRequest(agentId, displayName, toolName, argsJson with secret-looking keys redacted, last 3 messages)` to `IApprover` (one grain per user).
- The Approver checks a memo table keyed by SHA-256(tool|args). On a miss, it asks a Fast-tier LLM judge, which returns allow, deny or ask. A deny publishes a ToolDenied event.
- **The 'ask' path:** the Approver stores a pending entry and publishes ApprovalRequested to an Orleans stream. The Telegram StreamSubscriber renders localized buttons `ap:{approvalId}:{allow_once|allow_thread|allow_user|deny}`, and the Approver awaits a TaskCompletionSource while holding `DelayDeactivation(5 min)`.
- When the user resolves it (with an owner check), an LLM summarizes the approval into a 'slightly broader' natural-language policy stored with scope Thread or User.
- `PolicyContextProvider` injects all policies into every prompt. Thread tools let the user say 'don't ask me about builds anymore' (AddApproverPolicy) or remove or list policies.
- Any failure of the approval grain means Deny, with telemetry counters.

**Strengths:** the scopes map directly to 'BackgroundRemover may run: once / in this chat / always'. Policy management in natural language is user-friendly. Fail-closed is the right default. Prompts are localized.

**Weaknesses:**
- Every uncached call costs an LLM judgment.
- Memo hits require identical arguments.
- Security is non-deterministic and exposed to prompt injection. LLM-broadened policies can over-grant.
- The pending wait is in memory, so it does not survive a restart. This contradicts the vision's BDD scenario 'Durable Human-in-the-Loop Approval'.

Adoption: keep the scopes, the 'ask' UX and the natural-language policy editing. Make the decision deterministic from the app's declared permissions and the user's grants, and use an LLM only to explain or phrase the request. Persist pending approvals, for example as a durable job or a stored completion source.
Evidence: E:\projects\IAW\src\Core\Agents\Agent.Authorization.cs:15-88; E:\projects\IAW\src\Core\Agents\Agent.cs:145; E:\projects\IAW\src\Agents\Security\ApproverAgent.cs:46-199,283-289,389-419; E:\projects\IAW\src\Core\Contracts\Security\IApprover.cs:5-68; E:\projects\IAW\src\Core\Contracts\Security\ApprovalPrompt.cs:17-34; E:\projects\IAW\src\Core\Context\PolicyContextProvider.cs:8-35; E:\projects\IAW\src\Telegram\Services\CallbackRouter.cs:102-127; E:\projects\IAW\src\Telegram\StreamSubscriber.cs:74-86; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:67-77,108-148; E:\projects\IAW\docs\architecture-review\vision.html:1053-1066 (BDD: Durable Human-in-the-Loop Approval)

### G10 (risk, high) [VERDICT: confirmed] Consent is easy to bypass: gating depends on parsing the grain key and applies only to LLM tool calls
`ResolveApproverGrainKey()` returns the user ID only when the grain key's first segment parses as a long (a Telegram ID). Otherwise it returns null, and null means no gating at all.

- Thread sub-agents (`{userId}/{slug}/{IInterface}`) are gated.
- Code-orchestration agents are keyed `task-{guid}/IShell` (IAWCluster.TaskId, ClusterClientExtensions.Get<T>(scope)), so they are never gated.
- The generated C# programs call typed grain methods directly (`shell.ExecuteAsync`, `fs.DeleteAsync`), which never go through the function-invocation middleware anyway.
- MCP's `agent_send_message` resolves agents by arbitrary ID.

On master the CodeOrchestrator runs LLM-generated C# with `dotnet run` and full machine access. On the orchestration-v5 branch, commit 1e37d99 was titled 'remove workspace restriction — agents have full PC access'.

User identity throughout is derived by string-parsing grain keys (Agent.ParseIdentityFromGrainKey), not carried as an authenticated principal.

Lesson for a marketplace with third-party and user-built apps: enforce permissions at the neuron operation boundary. Options are an Orleans incoming grain-call filter or a gateway neuron, working from an authenticated caller/user/app context propagated through RequestContext and checked against manifest-declared permissions and user grants. Enforcing at the LLM tool layer alone is not enough.
Evidence: E:\projects\IAW\src\Core\Agents\Agent.Authorization.cs:27-44; E:\projects\IAW\src\Core\Agents\Agent.cs:67-110; E:\projects\IAW\src\Aspire.Client\IAWCluster.cs:17-34; E:\projects\IAW\src\Core\Extensions\ClusterClientExtensions.cs:7-11; E:\projects\IAW\src\Agents\Orchestration\CodeOrchestratorAgent.cs:44-160 (generated-code instructions, typed calls); E:\projects\IAW\src\MCP\Tools\AgentTools.cs:13-22; git log origin/orchestration-v5: 1e37d99 'feat: remove workspace restriction — agents have full PC access'

### G11 (gap, high) [VERDICT: partially] No billing, credits or pricing in IAW; only token telemetry, which is the seam to meter Compute from
A grep of code, docs and website finds no price, cost, credit, budget, quota, tariff, marketplace, manifest, tenant or billing concept.

What exists:
- `UsageCaptureChatClient`, an IChatClient decorator that records input/output/total tokens from both streaming and non-streaming responses. It keeps them in a volatile `LastUsage` field per grain.
- OTel spans tagged `gen_ai.usage.input_tokens/output_tokens` and `gen_ai.agent.id`.
- Metrics: TokenUsage histogram, TotalInputTokens and TotalOutputTokens per agent.
- A durable per-agent `AgentEvent("LlmCall")` recording only the prompt length.

`LLMModel` has Id, Provider, DisplayName and Capabilities, but no price. Agents request abstract tiers (`[Llm<Fast>]`, Balanced, Reasoning), and the AppHost maps tiers to concrete models (`.WithLLM<Gpt54Nano>().AsFast()`). This is a natural pricing indirection: apps declare a tier and the operator sets the price. Note, however, that the owner removed tiers in DigitalBrain's provider layer (see memory: 'no tiers').

Recommendation:
- Reuse the decorator pattern as the Compute meter at the IChatClient and neuron-operation boundary.
- Attribute every charge to (user, app/neuron, operation, model).
- Write to a durable ledger grain, not to a volatile field or to telemetry only.
- Price from model metadata (per 1M input/output tokens), with app tariffs declared in the manifest and approved through the consent flow (G9).
Evidence: E:\projects\IAW\src\Core\AI\UsageCaptureChatClient.cs:7-61; E:\projects\IAW\src\Core\Contracts\AgentUsage.cs:4-7; E:\projects\IAW\src\Core\Agents\Agent.cs:36,216-228,300-322; E:\projects\IAW\src\Core\AI\LLMModel.cs:3-72; E:\projects\IAW\src\Core\AI\ModelTiers.cs:3-16; E:\projects\IAW\src\Aspire\AppHost.cs:6-12; E:\projects\IAW\src\Aspire.Hosting\LLMModelBuilder.cs:14-26

> CORRECTION: The absence of any pricing or billing concept is confirmed. The one near-exception is 'multi-tenant' in the 2026-03-17 spec :350. LLMModel has no price, and tiers map via LLMModelBuilder.cs:12-28.

The finding overstates the usage capture it proposes to reuse:
(1) UsageCaptureChatClient wraps the raw client BEFORE AsAIAgent (Agent.cs:36,129). MAF's ChatClientAgent adds FunctionInvokingChatClient on top by default (https://learn.microsoft.com/agent-framework/concepts/agents/agent-pipeline). The capture therefore sits under the tool loop, and `_lastUsage` is overwritten on each model round, so multi-round tool turns report only the last round's tokens.
(2) About 15 LLM call sites bypass it entirely because they use the raw `ChatClient` (Agent.cs:57): the Approver (ApproverAgent.cs:325,405,437), AgentSelector :53, TelegramUIAgent :43, CodeOrchestrator :365,396, the Thread's digest and summaries (:418,459), HistorySummarizer :84, Scheduling :121, Roslyn :181,263, Validator :49 and Explainability :74.
(3) Embeddings are never metered.

The complete per-call seam is the M.E.AI OpenTelemetry layer in the per-model client factory (LlmRegistration.cs:113-128, UseOpenTelemetry). A Compute meter belongs there, keyed by model, plus a caller context, not in a per-grain field.

### G12 (fact, high) [VERDICT: confirmed] Memory went from 5 memory agents to one automatic context provider with provenance
Commit a04ea9e (2026-04-20) deleted:
- UserMemory, ProjectMemory, EpisodeMemory, PatternMemory and CodeMemory agents, together with MemoryEntry (TrustScore, MemorySource provenance, Observe, Search, Consolidate, Decay, Forget);
- PreferenceAgent and KnowledgeAgent.

The commit message calls them 'over-partitioned'. They were replaced by `IawMemoryProvider`, a Microsoft Agent Framework `MessageAIContextProvider` attached to every agent:
- one Qdrant collection per user, `user-memory-{userId}`;
- after each turn it embeds and stores every user and assistant message, with payload content/role/threadId/createdAtTicks/sourceTelegramMsgId;
- before each turn it injects the top-5 similar entries as a `## Memories` system message;
- `LookupOriginAsync` powers the Thread `Explain` tool, which returns 'On {date} you said …' and emits a ForwardMessageHint so Telegram re-forwards the original message. That is provenance back to the source message.

Separately, RAGContextProvider searches the per-project collection built from ingested PDF chunks, and UserContextProvider injects the UserProfile preferences dictionary.

**Strengths:** no memory agents to route to; the standard Microsoft Agent Framework hook is used; the explainability UX is good.

**Weaknesses:**
- Raw turns are stored, including secrets and PII, with no dedupe, TTL, forget or trust score.
- Every turn costs one embedding call to recall and one to store.
- All memory is per user; there is no memory of neurons or apps.
- The website still documents the deleted agents.

Adoption for IntoChat 'memory of neurons': use the automatic-provider shape, but store typed facts with provenance (neuronId, operation, message ID, app ID). Bring back trust and decay from the old MemoryEntry design, and index system components in the same store but in separate collections.
Evidence: E:\projects\IAW\src\Core\Memory\IawMemoryProvider.cs:11-172; E:\projects\IAW\src\Core\Memory\MemoryHit.cs:3-8; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:79-103; E:\projects\IAW\src\Core\Context\RAGContextProvider.cs:8-50; E:\projects\IAW\src\Core\Context\UserContextProvider.cs:8-33; E:\projects\IAW\src\Core\Agents\Agent.cs:125-139; git show a04ea9e (commit message: 'Delete the five over-partitioned memory agents …'); E:\projects\IAW\website\guide\memory.md:1-120 (stale: documents deleted MemoryEntry/memory agents); https://learn.microsoft.com/agent-framework/concepts/agents/conversations/context-providers

### G13 (gap, high) [VERDICT: confirmed] No typed data or secret handling: IAW has no answer to 'store my date of birth or password and use it later'
**Untyped state.** Agent state is `IDurableDictionary<string, StateEntry>`, where `StateEntry(string Key, object Value)` is an untyped bag. ThreadAgent even stores callback routes as a pipe-delimited string. UserProfile preferences are `IDurableDictionary<string,string>`.

**Secrets.** The only secret handling is a regex that redacts JSON keys containing password, token, secret, apikey or authorization, plus Bearer tokens. It applies only to the arguments sent to the Approver LLM.

**Raw storage.** The memory provider embeds and stores raw message text, so a password or date of birth typed in chat lands in Qdrant and in LLM context.

**No schemas.** There is no value type, no sensitivity classification, no validation and no per-value consent.

For the owner's `Text : ValueNeuron<string>` idea, IAW's lesson is that typing has to come with:
- a closed set of value kinds (Text, Date, Number, Email, Secret…) shared with UI field kinds (G4);
- a sensitivity label, so Secret values are never embedded, logged, traced or sent to an LLM; only a handle or reference flows and is resolved inside the consuming neuron;
- consent attached to reading or using the value (who, which app, which purpose, which scope), reusing the once/thread/user scopes from G9;
- provenance: where the value came from and when.
Evidence: E:\projects\IAW\src\Core\Contracts\StateEntry.cs:4-6; E:\projects\IAW\src\Core\Contracts\AgentDurableState.cs:5-15; E:\projects\IAW\src\Core\Contracts\UserProfileDurableState.cs:5-10; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:358-363; E:\projects\IAW\src\Core\Agents\Agent.Authorization.cs:15-18,108-118; E:\projects\IAW\src\Core\Memory\IawMemoryProvider.cs:65-132

### G14 (risk, high) [VERDICT: confirmed] 'Every agent is a durable grain' was true in code but false in runtime: production used volatile journaling and memory storage
Every IAW agent inherits `DurableGrain` (Orleans.Journaling, version 10.0.1-alpha.1), with IDurableDictionary/IDurableList for history, the key-value state, the event log and scheduled jobs. UISession, UserProfile, EventRouter and TaskLedger are durable grains too. So 'can every neuron be a durable grain' was answered yes, by one base class.

However, the production silo registers `VolatileStateMachineStorageProvider`, and the Aspire topology uses `WithMemoryGrainStorage`, `WithMemoryStreaming` and `WithMemoryReminders`. Only DurableJobs (Azure Blob), Qdrant and Blob are really persistent. Conversation history, approval policies, UI sessions and pending approvals are all lost on restart, while the README advertises 'Durable state — … survive restarts'. The vision doc's 'What's Missing' section even lists 'Memory-only stream provider — all events lost on silo restart'.

Microsoft positions `IPersistentState<T>` as the standard persistence API and MemoryStorage as 'only for debugging and unit testing'. DigitalBrain neurons today use `IPersistentState<TState>`, apart from an Orleans journal hosting adapter.

Recommendation:
- Make persistence a per-neuron-kind decision in the SDK: value and entity neurons get real storage; UI and session neurons are explicitly ephemeral with a TTL.
- Never let 'durable' silently fall back to volatile; add a startup health check that fails when volatile or memory providers are configured outside tests.
- Treat journaling (still alpha) as an opt-in for neurons that need event history or audit, not as the universal default.
Evidence: E:\projects\IAW\src\Core\Agents\Agent.cs:30-34; E:\projects\IAW\src\Aspire.Client\IAWSiloExtensions.cs:34-35; E:\projects\IAW\src\Aspire.Hosting\IAWHostingExtensions.cs:15-22; E:\projects\IAW\Directory.Packages.props:21 (Microsoft.Orleans.Journaling 10.0.1-alpha.1); E:\projects\IAW\README.md ('Durable state — agent memory, conversation history, and project state survive restarts'); E:\projects\IAW\docs\architecture-review\vision.html (What's Missing: 'Memory-only stream provider'); src/Modules/DigitalBrain/Kernel/Aspire/AzureOrleansJournalHosting.cs; https://learn.microsoft.com/dotnet/orleans/resources/best-practices#storage-providers

### G15 (pattern, high) App format precedent: NuGet packages of typed interfaces, generated C# 'apps' that call the cluster, and agents exposed over OpenAI and MCP
IAW's extensibility unit was an assembly shipped as a NuGet package. IAW.Core, IAW.Agents, IAW.Agents.CSharp, Aspire.Hosting.IAW, Aspire.IAW.Client and IAW.Testing are all IsPackable. It contained an IAgent-derived interface (manifest plus tools) and an `Agent<TContract>` implementation, loaded in-process by the silo and discovered by reflection at startup. Hosting is one line: `builder.AddIAW().WithLLM<T>()...`, and `WithReference(iaw)` flows connection strings and keys.

There were also two other 'app' forms:

**Ephemeral generated apps.** CodeOrchestratorAgent (Reasoning tier) writes a standalone C# console program with the prescribed boilerplate `using var iaw = await IAWCluster.Connect(args); iaw.Get<IShell>(taskId)…`. The program calls typed agent methods, writes result.json, and is compiled and run with `dotnet build/run` in a temp directory. There is a compile-retry loop (max 2) with CodeValidator sanitizing hallucinated namespaces. The csproj is found by walking up to the IAW repo root (ScriptGenerator).

**Protocol exposure.** DevUI maps every IAgent interface to OpenAI Responses and Chat Completions endpoints (Microsoft Agent Framework DevUI). The MCP server exposes agent_list_all, assistant_chat, agent_send_message and agent_get_events.

**Weaknesses:** no isolation or sandboxing, no versioning, no runtime install or uninstall, no signed or declared permissions, and the generated-app path depends on repo layout.

Adoption: keep 'typed interface package = app contract' and 'standard protocols (MCP, OpenAI Responses) as external surfaces'. Add a packaged manifest (G7), runtime install into the catalog (G6), isolated execution for untrusted third-party or user code, and permission and tariff declarations.
Evidence: E:\projects\IAW\src\Core\Core.csproj:8-10; E:\projects\IAW\src\Agents\Agents.csproj:9-10; E:\projects\IAW\src\Testing\Testing.csproj:4-5; E:\projects\IAW\src\Aspire.Hosting\IAWHostingExtensions.cs:11-60; E:\projects\IAW\src\Agents\Orchestration\CodeOrchestratorAgent.cs:14-160; E:\projects\IAW\src\Core\Orchestration\ScriptGenerator.cs:5-68; E:\projects\IAW\src\Core\Orchestration\CodeValidator.cs:5-80; E:\projects\IAW\src\DevUI\Program.cs:14-33; E:\projects\IAW\src\DevUI\AgentDiscovery.cs:12-35; E:\projects\IAW\src\MCP\Tools\AgentTools.cs:24-50

### G16 (pattern, medium) Routing and token hygiene: direct routing, a non-LLM event router, compact results and Fast-tier agents
The 'Agent Team v2' commit (dd2f9d6) claims an 88% token reduction from:
- a direct `SendToAgent(displayName)` tool, so single-agent requests no longer go through selector plus code generation (the old tool was renamed Delegate to Orchestrate);
- filtering LLM-wrapper agents out of selector candidates, and filtering the orchestrator catalog down to the selected agents;
- moving simple agents to the Fast tier (reverted for DotNet because Nano was too weak at tool reasoning);
- truncating tool results (SendToAgent at 4000 characters, GetResponse at 8000);
- a three-layer instruction template and engineered [Description] text.

Elsewhere:
- `EventRouterGrain` routes mechanical events with durable pattern rules and no LLM (BuildFailed with CS0246 goes to filesystem 'fix'; TestFailed goes to dotnet 'diagnose'; HealthCritical goes to thread 'escalate').
- A scheduled Aspire log-cleanup job was designed because accumulated logs made traces unreadable.
- The TaskLedger digest summarises long tasks periodically instead of forwarding context.

Relevance to IntoChat's 'Show me all customers' flow and its 'trash in Aspire traces': use deterministic routing for mechanical steps (schema read, then read-only SELECT, then table view), return compact results with handles, and let an LLM choose only among a shortlist.
Evidence: git show dd2f9d6 -s (Agent Team v2: 88% token reduction, direct agent routing); E:\projects\IAW\docs\superpowers\specs\2026-03-23-agent-team-v2-design.md:194-267; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:53-66,158-203; E:\projects\IAW\src\Core\Grains\EventRouterGrain.cs:8-45; E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:426-478 (task digest); E:\projects\IAW\src\Core\Agents\Agent.cs:50-53 (MaxResponseLength 8000)

### G17 (pattern, high) Testing: TestCluster harness and architecture-guard tests worked; the BDD specs were never executable and drifted
`IAW.Testing.AgentTest<TAgent>` starts an Orleans TestCluster with memory storage, memory streams, in-memory reminders and durable jobs, volatile journaling, a MockChatClient that returns 'mock-response' (also supporting ReturnsStream and ThrowsOnSend), and a MockEmbeddingGenerator. Test IDs are unique per run. The suite has about 411 [Fact]/[Theory] across Core, Integration and E2E tests. ArchitectureGuard tests enforce structural rules by reflection: events implement IEvent, task events carry TaskId, LLM agents extend LlmAgentBase, and every agent has a matching interface.

IAW's CLAUDE.md admits mocks can't prove agent behavior. It requires live verification through the `iaw` MCP (assistant_chat, agent_get_events) plus Aspire traces.

BDD appears only as 7 Gherkin features embedded in docs/architecture-review/vision.html, covering finance, explainability, preferences, durable approval, ledger, self-improvement safety and the Excel agent. There is no Reqnroll or SpecFlow and no .feature files. Several scenarios describe things that never existed (PreferenceAgent, a durable approval that survives restart, the Excel agent).

For the owner's idea of 'app describes itself with Reqnroll features, discoverable by vector search': Gherkin scenarios are an excellent semantic description to index, but only if they are executable acceptance tests in the package. IAW shows that prose-only specs drift away from reality.
Evidence: E:\projects\IAW\src\Testing\AgentTest.cs:14-90; E:\projects\IAW\src\Testing\MockChatClient.cs:5-50; E:\projects\IAW\test\Core.Tests\ArchitectureGuardV2Tests.cs:11-60; E:\projects\IAW\CLAUDE.md (Testing strategy — prefer behavioral tests; iaw MCP verification loop); E:\projects\IAW\docs\architecture-review\vision.html:1000-1113 (BDD Specifications section); grep: no reqnroll/specflow/.feature in E:\projects\IAW

### G18 (pattern, high) Conversation scoping: threads mapped to Telegram forum topics, with per-thread sub-agent instances
A Thread grain is keyed `{userId}/{slug}`, and each thread maps to a Telegram forum topic. There are built-in General, Personal and IAW topics, users can create more, and topics are auto-renamed from an LLM-generated title. Sub-agents are addressed as `{threadId}/{IInterface}`, so every conversation gets isolated agent instances with separate history and state. Orchestration uses `task-{guid}/{IInterface}`, and one-off agents use `{IInterface}-{guid8}`. The spec formalized three scopes: task, user and ephemeral. History is capped per agent (Thread keeps 20 messages; the default is 100) and summarized at 40 messages.

**Strengths:** cheap isolation, and a natural mapping of 'app instance per conversation'.

**Weaknesses:**
- Identity and security depend on parsing these strings (G10).
- Dynamic IDs accumulate persisted state with no cleanup; spec section 14 flagged this.

For IntoChat, this is a precedent for scoping app installs and activations (per user, per workspace, per conversation) so that consent scopes and Compute attribution can use the same scope keys. Identity should be carried in typed context, not parsed from keys.
Evidence: E:\projects\IAW\src\Agents\Orchestration\ThreadAgent.cs:173-175,398-424; E:\projects\IAW\src\Core\Agents\Agent.cs:67-110; E:\projects\IAW\src\Telegram\Services\CommandHandler.cs:20,96; E:\projects\IAW\src\Telegram\Services\ResponseStreamer.cs:199-220; E:\projects\IAW\docs\superpowers\specs\2026-03-19-agent-registry-orchestration-redesign.md:110-170

### G19 (trash, high) [VERDICT: partially] Parts of IAW not to port: dead code, stale docs and over-claims
Do not port these as-is:
- **Unused UI machinery:** the UISession wizard, paginator, menu and form engines (tested, never called); CardPart, FormPart, ProgressPart and TextStyle; `ThreadAgent.RegisterCallback` and the IAgent.HandleCallback routing (never wired up); DashboardRenderer, ProjectDashboard and DashboardChangedEvent (never rendered); the 'ui-notifications' broadcast channel (registered, never consumed).
- **ConsiliumResponseEvent** plus the website consilium guide, which describes multi-model voting that has no implementation.
- **13 LLM wrapper agents**, one per model (Gpt54MiniAgent and others), which were then filtered out of routing anyway.
- **Stale claims in the README:** '65+ agents' when there are about 17 working agents plus 13 wrappers, and durable state (G14).
- **Stale docs:** website/guide/memory.md and agents.md describe the deleted memory agents.
- **Contradictory spec:** the 2026-03-19 spec says the registry is Qdrant-backed (it is in-memory) and that UISession 'is reduced or eliminated' (it still has 6 dictionaries).

Treat IAW docs as statements of intent, and read the code to know what exists.
Evidence: E:\projects\IAW\src\Agents\UI\UISession.cs:135-363; E:\projects\IAW\src\Core\Contracts\DashboardRenderer.cs:5-49; E:\projects\IAW\src\Core\Contracts\Events\DashboardChangedEvent.cs:4-7; E:\projects\IAW\src\Aspire.Client\IAWSiloExtensions.cs:37; E:\projects\IAW\src\Core\Messages\Events\ConsiliumResponseEvent.cs:4; E:\projects\IAW\src\Agents\LLM\Gpt54MiniAgent.cs:9-12; E:\projects\IAW\README.md ('65+ agents'); E:\projects\IAW\website\guide\memory.md; E:\projects\IAW\website\guide\agents.md:127-151

> CORRECTION: Most items are confirmed:
- UISession's engines are called only from tests.
- ThreadAgent.RegisterCallback has no callers, and Thread.HandleCallback is exercised only in tests.
- DashboardRenderer, ProjectDashboard and DashboardChangedEvent are unreferenced outside their definitions and tests.
- The 'ui-notifications' broadcast channel is registered but never consumed.
- ConsiliumResponseEvent is only defined (and website/guide/consilium.md exists).
- The README says '65+ agents'.
- UISessionDurableState has exactly 6 dictionaries.
- The spec says the registry is Qdrant-backed (:22,177).
- There are 17 non-wrapper agent classes.

Corrections:
(1) There are 14 LLM wrapper agents, not 13: Claude45Haiku, Gemini31, Gpt4o, Gpt4oMini, Gpt52, Gpt53, Gpt54, Gpt54Mini, Gpt54Nano, GrokLatest, Llama32, Opus46, Qwen25 and Sonnet46 in src/Agents/LLM.
(2) They are NOT 'filtered out of routing anyway'. AgentSelector drops namespace 'models' (AgentSelectorAgent.cs:26), and the CodeOrchestrator prompt says to ignore them. But the Thread's AgentRoutingContextProvider excludes only four orchestration interfaces (AgentRoutingContextProvider.cs:14,42-45), and the wrappers carry capabilities such as 'reasoning', 'generation' and 'fast' (e.g. src/Core/AI/Models/OpenAI/Gpt54Mini.cs:15-17). They can still be picked through SendToAgent.

### G20 (recommendation, medium) Adopt-or-avoid summary for IntoChat/DigitalBrain
**Adopt, adapted:**
1. A closed, semantic UI part vocabulary with renderer-specific fallbacks, and 'explicit UI tool beats inference' (G1, G2), applied to IntoChat's stateful component neurons.
2. Interface-as-manifest plus routing examples plus hybrid vector/keyword shortlist then LLM choice, extended to a persistent, versioned, three-level catalog of apps, neurons and operations, including Gherkin scenarios (G5, G6, G7).
3. Approval scopes once/thread/user, natural-language policy management, fail-closed, and localized approval buttons. Enforcement should be deterministic and durable at the neuron boundary (G9, G10).
4. An IChatClient usage decorator turned into a durable Compute ledger with model pricing and app tariffs (G11).
5. Automatic memory through an AIContextProvider with provenance back to the source message, storing typed facts rather than raw turns (G12).
6. The TestCluster harness, architecture-guard tests and executable Gherkin (G17).
7. Non-LLM routing for mechanical steps and compact tool results (G16).

**Avoid:**
- regex/LLM inference of UI after the fact as the main path;
- sending clicks back to the LLM as text;
- a per-call LLM security judge;
- volatile storage behind 'durable' APIs;
- running generated code in-process with full access;
- identity parsed from grain keys;
- a second string-returning tool API beside each typed one (G8).
Evidence: see G1-G19 evidence


## Missed (from verifier)
- DotNet agent capability hole. IDotNet.BuildAsync, TestAsync and RunAsync return typed records, which IsToolSafeReturnType filters out (Agent.Tools.cs:110-113,170-183). DotNetAgent adds no DefineTools wrappers (grep: DefineTools overrides exist only in the Roslyn, Aspire, FileSystem, Git and Shell agents). The DotNet agent's LLM therefore cannot build, test or run, even though its instructions tell it to (src/Agents.CSharp/DotNet/IDotNet.cs:21-33). This is concrete evidence for a one-surface typed-tool SDK.
- UI parts are not composed across agents. ThreadAgent.SendToAgentAsync keeps only TextPart text and MediaPart deliveries from a sub-agent's AgentResponse (src/Agents/Orchestration/ThreadAgent.cs:186-188). The sub-agent's pending UI hints (ProposeOptions, Agent.Tools.cs:38-56) are never drained, so only the top-level Thread can show buttons. For IntoChat this means app/neuron UI must flow through a composition contract, not through per-agent side channels.
- How IAW's LLM actually generates UI (part of the scope question, and not in the reviewed findings). It is a two-stage pipeline. First, a deterministic RichContentParser turns numbered lists into options (at most 8, labels cut at 40 characters) and blob URLs into media. Second, for texts of 300+ characters only, it falls back to a Fast-tier TelegramUIAgent that rewrites the whole answer into Telegram HTML plus JSON parts (src/Telegram/Formatting/TelegramFormatter.cs:11-44; src/Agents/Orchestration/ITelegramUI.cs:16-61). The formatter is a renderer-specific LLM post-pass, not a renderer-agnostic generation step. That is a useful anti-pattern to note for IntoChat, which should generate semantic parts once and render them per client.
- Token metering undercounts. UsageCaptureChatClient sits below MAF's default FunctionInvokingChatClient, so `_lastUsage` is overwritten on each model round. About 15 direct ChatClient.GetResponseAsync call sites bypass it: Approver, AgentSelector, TelegramUI, CodeOrchestrator, HistorySummarizer, Scheduling, Roslyn, Validator, Explainability and the Thread digest. The correct seam is the per-model client factory with UseOpenTelemetry (src/Aspire.Client/LlmRegistration.cs:113-128). Source: https://learn.microsoft.com/agent-framework/concepts/agents/agent-pipeline ('By default, ChatClientAgent wraps the provided chat client with function-calling support').
- EnableSensitiveData is hard-coded to true for all LLM telemetry (src/Aspire.Client/LlmRegistration.cs:124-126), so full prompts and completions go into traces. This is relevant to both the secret/PII question (G13) and the owner's complaint about trace noise.
- Memory pollution from sub-agents. IawMemoryProvider runs on every agent (Agent.cs:125-128), and sub-agents keyed `{userId}/{slug}/{IInterface}` resolve a userId (Agent.cs:78-110). Thread-authored delegation prompts are therefore stored as the user's 'user'-role memories, and the Explain tool can misattribute them ('you said…', ThreadAgent.cs:102).
- LLM wrapper agents leak into Thread routing. AgentRoutingContextProvider does not filter the 'models' namespace (AgentRoutingContextProvider.cs:14,42-45). When nothing matches, it injects the entire catalog (about 31 agents) into the system prompt (:47-54). Together with substring keyword matching (AgentRegistryGrain.cs:139), this is IAW's version of IntoChat's 'too much trash' problem.
- Testing: IAW has no BDD layer. There are no .feature files and no Reqnroll or SpecFlow references. Tests are xUnit in test/Core.Tests, test/Integration.Tests and test/E2E.Tests, using an Orleans TestCluster harness (src/Testing/AgentTest.cs, MockChatClient.cs, MockEmbeddingGenerator.cs). Reflection-based architecture guard tests (test/Core.Tests/ArchitectureGuardV2Tests.cs:16-59, e.g. All_agents_have_matching_IAgent_derived_interfaces, LLM_agents_extend_LLM_base) are the reusable idea: they could enforce marketplace manifest completeness (description, capabilities, examples, tariffs, permissions) at build time. Note that IAgent's static-virtual metadata (IAgent.cs:7-11) is effectively a compile-time manifest.
- The spec had already identified the embedding-dimension risk and said the startup task should validate it (docs/superpowers/specs/2026-03-19-agent-registry-orchestration-redesign.md:790). The implementation never did. This is another instance of the pattern that IAW docs describe intent rather than what the code does.

## Open questions
- Is Telegram (or any non-Flutter client) a target surface for IntoChat apps? If yes, should the SDK define a closed UI part vocabulary with per-renderer fallbacks (IAW's UIPart idea), or is Flutter the only renderer and Telegram out of scope?
- Should consent be decided deterministically from manifest-declared permissions plus user grants, with an LLM only phrasing the question? Or is an IAW-style LLM judge acceptable for actions an app did not declare?
- Which approval scopes should marketplace apps get (once / this conversation / always, as in IAW), and do they apply per app, per operation, or per Compute spend ceiling?
- Compute pricing granularity: IAW used abstract model tiers (Fast/Balanced/Reasoning) mapped to concrete models, but DigitalBrain deliberately dropped tiers. Should apps declare a concrete model, a tier, or nothing, with the user or operator paying the concrete model's rate?
- Memory policy: may raw conversation turns (which can contain passwords, dates of birth or PII) be embedded into Qdrant as IAW did, or must memory store only typed, sensitivity-classified facts with provenance, with secrets never embedded or sent to an LLM?
- Durability default for neurons: persistent by default with explicit opt-in to ephemeral, or the reverse? And should Orleans journaling (still alpha in IAW) be allowed as the default storage, or only IPersistentState?
- Should an app package be required to ship executable Gherkin/Reqnroll scenarios, used both as its searchable 'what I do' description and as acceptance tests, given that IAW's prose-only BDD drifted from reality?
- Execution isolation for third-party, user-built and generated apps: in-process assemblies (IAW's model) or isolated processes/containers with a capability gateway? IAW's code orchestrator ran LLM-generated C# with full machine access.