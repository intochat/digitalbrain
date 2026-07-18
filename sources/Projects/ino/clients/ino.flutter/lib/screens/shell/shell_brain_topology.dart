import 'package:flutter/material.dart';

/// Static topology shared by the brain canvas, demo runner, and inspector.
/// Sourced verbatim from `docs/ino-design/src/data.js` (CLUSTERS + ALIAS) and
/// `docs/ino-design/src/brain.js` (filamentPairs).

class DrawerEvent {
  const DrawerEvent({
    required this.t,
    this.from,
    this.to,
    required this.payload,
    this.recall = false,
  });

  /// Display timestamp like "now", "+0.4s", "−2m".
  final String t;

  /// [from] is an inbound synapse; [to] is an outbound one. At most one is set.
  final String? from;
  final String? to;

  final String payload;

  /// Recall fires render with gold tone.
  final bool recall;
}
class ShellCluster {
  const ShellCluster({
    required this.id,
    required this.label,
    required this.domain,
    required this.position,
    required this.color,
    required this.size,
    required this.aliases,
  });

  final String id;
  final String label;
  final String domain;
  final ({double x, double y, double z}) position;
  final Color color;
  final double size;
  final List<String> aliases;
}

class ShellNeuron {
  const ShellNeuron({
    required this.id,
    required this.alias,
    required this.cluster,
    required this.domain,
    required this.color,
  });

  final String id;
  final String alias;
  final String cluster;
  final String domain;
  final Color color;
}

class ShellTopology {
  ShellTopology._();

  static const List<ShellCluster> clusters = [
    ShellCluster(
      id: 'cortex', label: 'CORTEX', domain: 'system',
      position: (x: 0, y: 0.20, z: 1.05),
      color: Color(0xFFE6EDF7), size: 0.34,
      aliases: ['Cortex'],
    ),
    ShellCluster(
      id: 'travel', label: 'TRAVEL', domain: 'travel',
      position: (x: 0.95, y: 0.55, z: 0.10),
      color: Color(0xFF7C8AFF), size: 0.28,
      aliases: ['PlanTrip','FindFlights','FindHotels','FindPlaces',
                'BookFlight','BookHotel','TripBudget','WeatherFit','Itinerary'],
    ),
    ShellCluster(
      id: 'recall', label: 'RECALL', domain: 'recall',
      position: (x: -0.85, y: 0.65, z: 0.40),
      color: Color(0xFFE8C56A), size: 0.26,
      aliases: ['Preferences','PriorTrips','PeopleGraph','StyleBias',
                'Pinned','Episodes','Aliases'],
    ),
    ShellCluster(
      id: 'location', label: 'LOCATION', domain: 'location',
      position: (x: 0.60, y: -0.65, z: 0.55),
      color: Color(0xFF6EE7A8), size: 0.22,
      aliases: ['Forecast','GeoIndex','TimeZone','Heatmap'],
    ),
    ShellCluster(
      id: 'reminders', label: 'REMINDERS', domain: 'reminders',
      position: (x: -0.55, y: -0.55, z: 0.70),
      color: Color(0xFFF4B8E4), size: 0.20,
      aliases: ['VisaReminder','Schedule','Followups','Snooze','Pulse'],
    ),
    ShellCluster(
      id: 'taxi', label: 'TAXI', domain: 'taxi',
      position: (x: 0.25, y: -0.95, z: -0.30),
      color: Color(0xFFFFD08A), size: 0.18,
      aliases: ['Hail','Surge','Driver','Eta'],
    ),
    ShellCluster(
      id: 'genesis', label: 'GENESIS', domain: 'genesis',
      position: (x: -1.00, y: 0.05, z: -0.20),
      color: Color(0xFFC9D6FF), size: 0.20,
      aliases: ['L1Forge','L2Sketch','L3Review','Sandbox','Schema','Catalog'],
    ),
    ShellCluster(
      id: 'identity', label: 'IDENTITY', domain: 'identity',
      position: (x: 0.05, y: 0.95, z: -0.45),
      color: Color(0xFFB8C5E0), size: 0.18,
      aliases: ['Self','Persona','Tenancy'],
    ),
  ];

  static List<ShellNeuron> get neurons {
    final out = <ShellNeuron>[];
    var nid = 0;
    for (final c in clusters) {
      for (final alias in c.aliases) {
        out.add(ShellNeuron(
          id: 'n${nid++}', alias: alias,
          cluster: c.id, domain: c.domain, color: c.color,
        ));
      }
    }
    return out;
  }

  static final Map<String, ShellNeuron> _aliasIndex = {
    for (final n in neurons) n.alias: n,
  };

  static ShellNeuron? aliasLookup(String alias) => _aliasIndex[alias];

  /// Filament pairs (faint static lines between clusters), from brain.js.
  static const List<(String, String)> filamentPairs = [
    ('cortex', 'travel'),    ('cortex', 'recall'),
    ('cortex', 'location'),  ('cortex', 'reminders'),
    ('cortex', 'taxi'),      ('cortex', 'genesis'),
    ('cortex', 'identity'),
    ('travel', 'recall'),    ('travel', 'location'),
    ('travel', 'reminders'),
    ('recall', 'identity'),
  ];
}

/// Hand-curated event lists per neuron alias for the inspector drawer.
/// Data ported verbatim from `docs/ino-design/src/data.js` lines 157–187.
extension ShellTopologyDrawerEvents on ShellTopology {
  static const Map<String, List<DrawerEvent>> _drawerEvents = {
    'PlanTrip': [
      DrawerEvent(t: 'now',   from: 'Cortex',      payload: 'plan_trip{Tokyo}'),
      DrawerEvent(t: '+0.4s', from: 'Preferences', payload: 'ryokanBias=0.62', recall: true),
      DrawerEvent(t: '+0.8s', from: 'Forecast',    payload: 'rain[d3]=0.78'),
      DrawerEvent(t: '+1.4s', to:   'FindFlights', payload: 'KBP→NRT mid'),
      DrawerEvent(t: '+1.4s', to:   'FindHotels',  payload: 'Tokyo rain-fit'),
      DrawerEvent(t: '+1.4s', to:   'FindPlaces',  payload: 'mood=rain'),
      DrawerEvent(t: '+4.2s', to:   'VisaReminder',payload: '3d ahead'),
      DrawerEvent(t: '−2m',   from: 'Cortex',     payload: 'refresh{cache}'),
      DrawerEvent(t: '−14m',  to:   'TripBudget', payload: 'tier=mid'),
      DrawerEvent(t: '−1h',   from: 'PriorTrips', payload: 'Bali ref', recall: true),
    ],
    'FindHotels': [
      DrawerEvent(t: 'now',   from: 'PlanTrip',  payload: 'rain-fit Tokyo'),
      DrawerEvent(t: '+1.1s', to:   'PlanTrip',  payload: '4 candidates'),
      DrawerEvent(t: '−5m', from: 'StyleBias', payload: 'onsen+0.41', recall: true),
    ],
    'Cortex': [
      DrawerEvent(t: 'now',   from: 'utterance', payload: '"Plan a 5-day Tokyo…"'),
      DrawerEvent(t: '+0.0s', to:   'PlanTrip',  payload: 'route → travel'),
      DrawerEvent(t: '−1m', from: 'utterance', payload: '"what time is it in Tokyo"'),
      DrawerEvent(t: '−1m', to:   'TimeZone',  payload: 'Asia/Tokyo'),
    ],
  };

  /// Returns the curated event list for [alias], or a single placeholder when
  /// the alias has no curated history.
  static List<DrawerEvent> eventsFor(String alias) {
    final list = _drawerEvents[alias];
    if (list != null) return list;
    return const [
      DrawerEvent(
        t: 'now',
        from: '—',
        payload: 'no traffic yet — interact to populate',
      ),
    ];
  }

  /// LlmNeuron prompt corpus by alias. Aliases not in the map are treated
  /// as pure-code neurons and rendered with a generic placeholder.
  /// Sourced verbatim from docs/ino-design/src/inspector.js lines 60–73.
  static const Map<String, String> _prompts = {
    'PlanTrip':
        'You are PlanTrip. Compose a multi-day itinerary that respects:\n'
        '  - budget tier ∈ {low, mid, high}\n'
        '  - weather constraints (rain-friendly = prefer indoor anchors)\n'
        '  - recall.preferences (ryokan > hotel chain · bias 0.62)\n'
        'Output: typed synapse PlanComposed { flights, stays, days[] }.',
    'FindHotels':
        'You are FindHotels. Rank stays in {city} by:\n'
        '  rain-friendly { onsen, indoor baths, walkable cover } + recall.style.',
    'Cortex':
        'You route natural language to one typed synapse from IDiscovery catalog.\n'
        'No decomposition. No self-creation. Unrouteable → UnroutedIntent.',
    'Forecast':
        'You are a pure-code neuron (no LLM). 7-day forecast lookup with cache.',
    'Preferences':
        'You are Recall.Preferences. Project pinned moments + decisions into bias scalars.',
    'VisaReminder':
        'Schedule a reminder synapse N days ahead of a trip departure.',
  };

  /// Returns the prompt corpus for a neuron, or null when the alias has no
  /// curated prompt (treated as pure-code).
  static String? promptFor(String alias) => _prompts[alias];
}
