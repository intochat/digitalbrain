name: memory
version: 1.0.0
desc: Memory
triggers: MemoryRecall
emits: MemoryRecallSynapse,UiSurface
observed-synapses: 0

on: MemoryRecall
  show card( "Memory $key", column( text( "$value" ) ) )
