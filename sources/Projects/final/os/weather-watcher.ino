name: weather-watcher
version: 1.0.0
desc: Weather watcher
triggers: WeatherQuery
emits: WeatherResult,UiSurface
region: widgets
pinned: true
order: 2
observed-synapses: 0

on: WeatherResult
  show card( "Weather $location: $summary", column( text( "$summary" ), text( "source: $sourceUrl" ) ) )
