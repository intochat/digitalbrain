name: kernel-tasks
version: 1.0.0
desc: Active tasks and reminders, always at hand
triggers: SetAlarm,AlarmFired,InspectKernelTask
emits: UiSurface,KernelTaskListed
region: widgets
pinned: true
order: 1
system: true
observed-synapses: 0

on: NeuronTelemetry when Event = "KernelTasksListed"
  show card( "Active KernelTasks (live from KernelTaskSupervisor)", column( text( " (dynamic list of task buttons - structure defined in this .ino file; neuron emits rich kerneltasks with real data) " ) ) )

on: SetAlarm
  show card( "⏰ Alarm", column( text( "$label • in $minutes mins" ), button( "Dismiss", DismissAlarm( id: "$id" ) ) ) )

on: AlarmFired
  show card( "⏰ Alarm Fired", column( text( "$label • FIRED" ) ) )
