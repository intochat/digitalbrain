# Seam 5 — done bar #5 (chat send/button leave MCP)

**Tip:** `500e6d9e` (MCP tools already address `IConversation`) · Flutter bind `ae4b43a3` · HTTP `02e843f3`.

## FINAL §11 destination
| Tool | Tip file | Destination |
|---|---|---|
| `send_chat_message` / `activate_chat_button` | `DigitalBrain.Mcp/ChatTools.cs` | **Conversations module export** (Modules lead; UI helps) |
| `read_chat_transcript` | `IntrospectionTools.cs` | Conversations `IConversation.Read` (confirm Introspection) |

## Tip honesty (addressing DONE)
- Send → `IConversation.Send(SendConversationMessage)` + wait tip `IChat` outgoing `Responded` (strangle)
- Button → `IButton` + `ChatButtons.OfferedInstanceName`
- Host still registers `.WithTools<ChatTools>()` inside `DigitalBrain.Mcp`

## Physical leave (in flight)
1. Modules(+UI): export public tool types from Conversations (proposed `src/Modules/Conversations/McpTools`)
2. Integrations: thin `DigitalBrain.Mcp` — drop local ChatTools; `WithTools` from module assembly
3. Keep `McpActor` / path constants host- or Sdk-side initially (no circular Mcp↔module ref)

## Non-goals
- AppHost FREEZE files
- Dissolving tip `IChat` journal in the same commit (strangle stays until Conversations owns Responded)
