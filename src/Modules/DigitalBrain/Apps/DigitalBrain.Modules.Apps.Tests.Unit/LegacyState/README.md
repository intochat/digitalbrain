# State before app lifecycle tracking

`app.orleans` was captured with the production AppState from commit ccf5208c9,
before adding IDs 12 and above. Capture.cs.txt records the capture through the
production Orleans storage serializer. Keep these bytes fixed. LegacyAppStateFacts
loads them into a real app neuron, installs, reactivates, replays and uninstalls.
