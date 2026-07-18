# SDK — where C# is allowed (v5)

> Hand-written C# exists in **exactly two places**: the kernel
> (`DigitalBrain.Runtime` + `DigitalBrain.Kernel`) and the SDK
> (`DigitalBrain.SDK`). Everything else is `.ino` — including authored
> neurons, generated neurons, marketplace bundles, and the user's own
> domain.

---

## 1. What the SDK is

`DigitalBrain.SDK` is **one C# assembly** containing **platform-access
neurons**. A platform-access neuron is one that cannot be expressed in
pure Ino because it needs:

- A native API (Win32, COM, P/Invoke)
- A vendor SDK (Google, Stripe, Whisper, OpenAI)
- A protocol (gRPC, HTTP, WebSocket, OS pipes)
- A perf-sensitive primitive (zero-copy file IO, vector math)

Anything that does **not** need one of those should be a pure `.ino`
neuron — even if shipped from the SDK repo.

---

## 2. Where it lives

```
sdk/
└── DigitalBrain.SDK/
    ├── DigitalBrain.SDK.csproj
    ├── Ai/
    │   ├── Chat.cs         ←  the C# sidecar
    │   ├── Chat.ino        ←  the contract + scenario + RFW
    │   ├── Embedding.cs
    │   ├── Embedding.ino
    │   ├── Transcribe.cs
    │   └── Transcribe.ino
    ├── Windows/
    │   ├── FileSystem.cs
    │   ├── FileSystem.ino
    │   ├── Process.cs
    │   └── Process.ino
    ├── Google/
    │   ├── Gmail.cs
    │   ├── Gmail.ino
    │   ├── Sheets.cs
    │   └── Sheets.ino
    ├── Canvas/
    │   ├── Diagram.cs
    │   ├── Diagram.ino
    │   └── …
    ├── Sqlite/
    └── Stripe/
```

**One project. One `.csproj`.** Subfolders are just folders — they are
not subprojects, not separate assemblies, not their own NuGet packages.
The v4 `DigitalBrain.SDK.Contracts` and `DigitalBrain.SDK.Mcp` projects
fold into this one.

---

## 3. The sidecar pattern

Every platform-access neuron is a **pair**:

### `Ai/Chat.ino` — the contract

```ino
neuron Ai.Chat
  "Microsoft.Extensions.AI chat client. Routes to whichever provider is configured."

  synapse Request(prompt: string, context: string = "")
  synapse Response(text: string, tokens: int)

  state model: string = "gpt-4o-mini"

  on Request(r):
    # Implementation provided by the .cs sidecar; this block is the contract.
    delegate

  rfw:
    column padding=12:
      heading "Chat ({model})"
      field "Model" binds model
      caption "Last response tokens: {lastTokens}"

  scenario "happy path":
    given chat-client returns "hello world"
    when Request(prompt: "hi")
    then Response emitted with text == "hello world"
```

### `Ai/Chat.cs` — the sidecar

```csharp
namespace DigitalBrain.SDK.Ai;

public sealed partial class ChatNeuron(IChatClient chat)
{
    public async Task<Response> OnRequest(Request r, CancellationToken ct)
    {
        var result = await chat.CompleteAsync(r.Prompt, ct);
        return new Response(result.Text, result.Tokens);
    }
}
```

The sidecar:

- Is `partial` — the runtime-generated half handles the boilerplate
  (grain interface, state, RFW emit, telemetry).
- Implements only the methods marked `delegate` in the `.ino`.
- Never reaches into the runtime — it's just a typed function.
- Can be unit-tested in isolation with no Orleans.

The `.ino` file is **the source of truth** for the contract, the RFW,
and the scenarios. The `.cs` is just the body of one (or more) handler.

---

## 4. How a sidecar gets wired in

`DigitalBrain.Runtime` does this at activation:

1. Parse `Ai/Chat.ino` ⇒ AST.
2. See `delegate` in handler ⇒ skip C# codegen for that handler.
3. Find `Ai/Chat.cs` co-located ⇒ Roslyn-compile it.
4. Emit the partial half — grain interface, state, telemetry — and
   compile that.
5. Link both halves into one assembly. Activate.

If the `.cs` is missing but a handler is `delegate`, activation fails
with `Neuron.SidecarMissing`. If the `.ino` says `delegate` but the
`.cs` doesn't define the matching method, Roslyn complains at compile
time — caught before activation.

---

## 5. Dependency injection (the only SDK DI seam)

Sidecars receive constructor-injected services via a tiny rule:
**parameters typed as known framework abstractions are auto-bound.**

| Parameter type | Source |
|---|---|
| `IChatClient`, `IEmbeddingGenerator<,>`, `ISpeechToTextClient` | `Microsoft.Extensions.AI` chain configured in `digitalbrain.cs` |
| `ILogger<T>` | `Microsoft.Extensions.Logging` |
| `IBrainContext` | The runtime — exposes `BrainId`, `Now`, `Cancellation`, `Synapse(...)` |
| `HttpClient` | Named HTTP factory keyed by sidecar FQN |
| Anything else | Compile error — DI is deliberately tiny |

No `Microsoft.Extensions.DependencyInjection` lattice for the SDK. No
keyed services beyond `HttpClient`. No `IServiceProvider` injected
anywhere. If you need it, it goes through `IBrainContext`.

---

## 6. The kernel-vs-SDK split

| Concern | Lives in `DigitalBrain.Runtime` | Lives in `DigitalBrain.SDK` |
|---|---|---|
| Ino parser, binder, codegen | ✔ | |
| Roslyn compile + load | ✔ | |
| Orleans grain lifecycle | ✔ | |
| Scenario runner | ✔ | |
| RFW envelope + lock states | ✔ | |
| BrainId namespacing | ✔ | |
| `Ai.*` neurons | | ✔ |
| `Google.*` neurons | | ✔ |
| `Windows.*` neurons | | ✔ |
| `Canvas.*` neurons | | ✔ |
| Storage primitives (`Sqlite`, `Memory`, `Vector`) | | ✔ |

`DigitalBrain.Kernel` (the *application* layer of the kernel, not the
substrate) hosts the user-facing meta-neurons: **Creator** (LLM →
`.ino`), **Navigator** (intent → handler dispatch), **Ino** (the
assistant app), **BrainRegistry** (lists brains), **Gateway** (gRPC to
the Flutter shell). These are mostly `.ino` themselves but several have
`.cs` sidecars for the LLM / gRPC plumbing.

---

## 7. Extending the SDK (the only hand-written-C# workflow)

To add `Windows.FileSystem.Read`:

1. **Write `sdk/DigitalBrain.SDK/Windows/FileSystem.ino`** declaring
   `synapse ReadRequest`, `synapse ReadResponse`, the handler marked
   `delegate`, RFW, scenarios.
2. **Write `sdk/DigitalBrain.SDK/Windows/FileSystem.cs`** as a
   `partial` class with `OnReadRequest(ReadRequest r) => ...`.
3. **Run `dotnet test inolang/DigitalBrain.InoLang.Tests`** — the
   scenario runner picks up the new neuron and exercises it.
4. **Done.** The SDK assembly rebuild auto-includes it; Aspire's
   `rebuild` on the kernel resource picks it up on next launch.

No `.csproj` edits. No `IServiceCollection` registration. No assembly
scanning configuration. The runtime reflects the SDK assembly at startup
for sidecars and the parser globs `*.ino` for contracts.

---

## 8. What the SDK **does not** do

- ❌ It does not own neurons that could be pure `.ino`. The
  reporting-aggregation example from the user (`"repeat 5 times,
  summarise each"`) is **not** an SDK neuron — it's an LLM-generated
  `.ino` that composes existing SDK primitives.
- ❌ It does not own the marketplace UI, the brain registry UI, or any
  Flutter widget. UI is in the Flutter shell's RFW kit and called from
  `rfw:` blocks.
- ❌ It does not own auth tokens — those live in
  `brains/{brainId}/auth/` via `IBrainContext`.
- ❌ It does not own the InoLang compiler — that's `DigitalBrain.Runtime`'s
  job, and the SDK depends on it (not vice versa).

---

## 9. NuGet versioning

`DigitalBrain.SDK` is published as a single NuGet package. Domains
declare `requires sdk >= 1.4` in their `manifest.ino` (see DOMAINS.md
§2). The runtime checks at install time and refuses to enable a domain
that needs an SDK version older than what's loaded.

That's the entire SDK versioning story. No semver dance per
sub-namespace. The SDK is one assembly, one version, one cadence.
