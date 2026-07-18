neuron DigitalBrain.WidgetCanvas.ReminderNeuron
  "Counts a reminder down; the panel's hands run backward and it pulses at zero."

  using remindMe = synapse(DigitalBrain.WidgetCanvas.RemindMe)
  using snooze   = synapse(DigitalBrain.WidgetCanvas.Snooze)
  using armed    = synapse(DigitalBrain.WidgetCanvas.ReminderArmed)
  using fired    = synapse(DigitalBrain.WidgetCanvas.Fired)

  state durationSeconds: int
  state startedAtUtc: string

  on remindMe:
    log "reminder: arming for {remindMe.minutes} minutes"
    emit armed(minutes: remindMe.minutes)

  on snooze:
    log "reminder: snoozed {snooze.minutes} minutes"
    emit armed(minutes: snooze.minutes)

  on fired:
    log "reminder: time's up"

  ui:
    CountdownClock(durationSeconds: durationSeconds, startedAtUtc: startedAtUtc, onZero: fired)

scenario "remind me arms the reminder"
  when synapse remindMe(minutes: 10)
  then synapse armed emitted with minutes == 10

scenario "snooze re-arms the reminder"
  when synapse snooze(minutes: 5)
  then synapse armed emitted with minutes == 5
