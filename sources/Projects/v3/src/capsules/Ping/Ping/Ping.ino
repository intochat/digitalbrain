neuron Ping.Echo
  "Answers every Ping with a Pong and announces it to whoever is listening."

  using ping = synapse(Ping.Contracts.Ping)     # handles ping  -> IHandle<Ping>
  using pong = synapse(Ping.Contracts.Pong)      # broadcasts pong -> IEmit<Pong>

  broadcasts pong
  handles    ping

  state lastSeen: text

  on ping:                                       # -> PingNeuron.HandleAsync(Ping)
    set lastSeen = ping.from
    emit pong(to: ping.from)                      # broadcast (RoutingMode.Broadcast)

  ui:                                            # -> RFW widget (deferred render)
    Card(title: "Echo", body: lastSeen)

scenario "a ping is echoed as a broadcast pong"  # -> PingSimulation (the gate)
  when  emit ping(from: "alice")
  then  broadcast pong observed with to == "alice"
