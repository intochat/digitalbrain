Feature: App-owned assistant turn stream
  The workspace assistant owns the browser turn protocol and retained conversation.

  Scenario: Stream text and retain it with thread and workspace isolation
    Then the application streams and retains an isolated answer

  Scenario: Replay is idempotent and conflicting input is rejected
    Then the application replays once and rejects conflicting run input

  Scenario: Unavailable models do not acquire a conversation
    Then an unavailable model leaves the conversation idle

  Scenario: Failed runs release the conversation
    Then a failed model turn permits a subsequent turn

  Scenario: Cancellation releases the conversation
    Then a cancelled model turn permits a subsequent turn

  Scenario: Tool events and cards travel with an intent receipt
    Then the application forwards tools cards and a receipt

  Scenario: Concurrent submissions cannot complete the owning run
    Then a conflicting active run cannot corrupt its owner

  Scenario: Explicit controls own submission and retained drafts
    Then explicit controls submit and restore thread drafts

  Scenario: Explicit controls cancel busy turns
    Then the stop button releases the running turn

  Scenario: File input captures attachment context without submitting
    Then a bound file input adds attachment content to the draft

  Scenario: App launches create independent workspace windows
    Then two assistant windows have independent conversation state

  Scenario: Legacy conversations migrate once without overwriting authoritative state
    Then legacy conversation restoration preserves server history and new drafts

  Scenario: Removed model profiles do not break restored windows
    Then a retired model profile does not prevent reopening

  Scenario: Production tool handles become reopenable result cards
    Then tool window handles without UI metadata become result cards

  Scenario: Tool cards retain their links and window actions
    Then tool card actions become explicit activation buttons

  Scenario: Long model answers remain complete and reopenable
    Then a long answer survives bounded chat projection and reopening

  Scenario: Historical artifact context stays out of visible conversations
    Then legacy artifact prompt suffixes remain only in durable history
