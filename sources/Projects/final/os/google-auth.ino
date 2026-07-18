name: google-auth
version: 1.0.0
desc: Google auth (L1)
triggers: BeginGoogleAuth,GoogleAuthCompleted,GmailLastSendersRequest,CapabilityDecision,CapabilityGrantRequest
emits: AuthLinkReady,GoogleAuthCompleted,GmailLastSendersResult,UiSurface,SaveFileRequest
observed-synapses: 0

on: CapabilityGrantRequest
  show card( "google-auth wants privileges", column( text( "This experience requests SaveFileRequest and GoogleApi access." ), button( "Allow", CapabilityDecision( BundleId: "google-auth", Allowed: true ) ), button( "Deny", CapabilityDecision( BundleId: "google-auth", Allowed: false ) ) ) )
