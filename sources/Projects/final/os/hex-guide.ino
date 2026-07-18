name: hex-guide
version: 1.0.0
desc: Hex guide
triggers: HexGuideRequest
emits: UiSurface
observed-synapses: 0

on: HexGuideRequest
  show card( "hex1b.dev/guide/", column( text( "Browse sections" ), button( "Architecture", GuideNavigate( section: "architecture" ) ), button( "Events", GuideNavigate( section: "events-streams" ) ), button( "Back", GuideNavigate( section: "index" ) ) ) )
