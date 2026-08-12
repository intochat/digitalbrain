# Seam 5 — done bar #5 (chat send/button leave MCP)

**Tip base:** `02e843f3` Conversation HTTP · Integrations slice lands Conversation addressing in MCP host.

## FINAL §11 destination
| Tool | Tip file | Destination |
|---|---|---|
| `send_chat_message` / `activate_chat_button` | `DigitalBrain.Mcp/ChatTools.cs` | UI / Conversations (module export later) |
| `read_chat_transcript` | `IntrospectionTools.cs` | Conversations `IConversation.Read` |

## Integrations slice (this land)
1. **Send** → `IConversation.Send(SendConversationMessage)` (HTTP `conversation.send` parity)
2. **Watch** tip `IChat` outgoing journal for `Responded` (strangle — same as `MapOwnerCommands.StreamConversationDeltasAsync`)
3. **Button** → `IButton` + `ChatButtons.OfferedInstanceName` (HTTP `chat.button` parity; was wrongly `FireAsync<IChat>`)
4. **Transcript** → `IConversation.Read()`
5. **Unblock tip:** `ConversationTranscript : Synapse` so `RequestSynapse<ConversationTranscript>` compiles (Modules Contracts hole on 02e843f3)

Residual: tools still live in `DigitalBrain.Mcp` until Modules/UI export MCP tool types and host goes thin — addressing honesty first.
