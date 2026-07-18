name: gmail-last-senders
version: 0.1.0
desc: Asks Gmail for your most recent senders and can save them to a file
triggers: GmailLastSendersRequest,AgentRequest
emits: GmailLastSendersResult,UiSurface,SaveFileRequest
requires: google-auth
region: widgets
pinned: true
order: 3
observed-synapses: 0

on: GrantRequested
  show card( "gmail-last-senders wants SaveFileRequest", column( text( "Allow save to file after Gmail fetch?" ), button( "Allow", GrantDecision( BundleId: "gmail-last-senders", Capabilities: "SaveFileRequest", Allowed: true ) ), button( "Deny", GrantDecision( BundleId: "gmail-last-senders", Capabilities: "SaveFileRequest", Allowed: false ) ) ) )

on: GmailLastSendersResult as resultAlias
  show card( "Gmail last senders", column( text( "senders from rule path only" ), button( "Save to file", SaveFileRequest( filePath: "gmail-senders.txt" ) ) ) )
