# brain.ino — the machine, as a file. Hashed, journaled, shareable. No secrets, no behavior.
name: vlad-brain
version: 1.0.0
desc: Vlad's DigitalBrain root world

llm: gemma3 as fast
llm: nemotron3-nano as reasoning
voice: whisper-local
durability: redis
ui: flutter windows autostart
discovery: on
advertised-ip: env DIGITALBRAIN_ADVERTISED_IP

seed: os/shell.ino
seed: os/marketplace.ino
seed: os/packager.ino
seed: os/creator.ino
seed: os/llm-agent.ino
seed: os/kernel-tasks.ino
seed: os/memory.ino
seed: os/weather-watcher.ino
seed: os/transcription.ino
seed: os/hex-guide.ino
seed: os/google-auth.ino
seed: os/gmail-last-senders.ino
seed: os/awesome-se-team.ino

world: example-world from os/example-world.ino
