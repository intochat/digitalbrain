name: transcription
version: 1.0.0
desc: Transcription
triggers: VoiceTranscribed
emits: AgentRequest,UiSurface
observed-synapses: 0

on: VoiceTranscribed
  show card( "🎤 Voice", column( text( "$text" ), button( "Ask", AgentRequest( prompt: "$text" ) ) ) )
