# Seam 5 — done bar #5 (chat send/button leave MCP)

**Tip:** `440f0a1a` · Integrations coordinates; Modules/UI own destination; Kernel owns HTTP maps.

## FINAL §11 destination
| Tool | Tip file | Destination |
|---|---|---|
| `send_chat_message` / `activate_chat_button` | `DigitalBrain.Mcp/ChatTools.cs` | UI / Conversations module tools |
| `read_chat_transcript` | `IntrospectionTools.cs` (related) | Introspection / Conversations (confirm with Modules) |

## Tip MCP shape (leave candidates)
- `McpSurface.SendChatMessage` / `ActivateChatButton` / `ReadChatTranscript`
- `ChatTools` → `NeuronId.For<IChat>` + `SendMessage` / `ButtonClicked`
- Registered in `Program.cs` `.WithTools<ChatTools>()`

## HTTP addressing (blocker honesty)
- Paths declared: `HttpSurfacePaths.KindConversationSend` / `KindConversationCancelTurn`
- **MapOwnerCommands tip still routes `chat.*` kinds via `IChat` only** — no `conversation.send` handler yet
- Conversation streams paths exist (`/conversations/{conversationName}/…`)

## Integrations hold
Do **not** delete/move ChatTools until Modules+Kernel have tip-true Conversation HTTP (or module-exported MCP tools) addressing so northbound send/button has a non-IChat home. B4 park already ForPrincipal conversation.

## Ask Modules
1. Who owns MCP tool registration after leave (Conversations module export vs UI)?
2. Rebind target: `IConversation.Send(SendConversationMessage)` + button path?
3. Go/no-go when `conversation.send` lands on MapOwnerCommands (Kernel) — Integrations thins host then.
