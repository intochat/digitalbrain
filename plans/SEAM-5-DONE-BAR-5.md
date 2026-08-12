# Seam 5 — done bar #5 (chat send/button leave MCP)

## Physical leave (landed)
| Item | Location |
|---|---|
| Conversation MCP tools | `src/Modules/Conversations/McpTools` (`ConversationTools`) |
| Tool names | `ConversationMcpSurface` |
| Shared principal helper | `DigitalBrain.Auth.McpActor` (was Mcp-internal) |
| Thin host registration | `DigitalBrain.Mcp` → `.WithTools<ConversationTools>()` only |

Host no longer contains `ChatTools` / chat DTOs / `read_chat_transcript`.

## Addressing (prior `500e6d9e`)
Send → `IConversation`; button → `IButton`; Responded watch tip `IChat` journal (strangle).

## Residual (not #5)
Other tool families (Registry/Time/Library/Introspection) still host-resident until their seams.
