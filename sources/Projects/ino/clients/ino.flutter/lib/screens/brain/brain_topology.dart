// Static brain topology for Slice B.1 of the brain visualisation.
//
// This is a representative snapshot of the ino neuron / synapse graph as it
// exists on master. B.2 will replace it with a live pull from
// IDiscovery.DumpAsync over gRPC; B.6 will overlay decay and live activity.
// Until then the dataset is hardcoded so the visual lands first.
//
// Node positions are deterministic — domains are anchored to brain-like lobe
// positions so the rendered cloud reads as a brain on first frame, not as
// "some 3D dots." Within a domain, nodes are placed via golden-angle spiral
// around the anchor.
//
// NeuronDefinition halos were removed in slice C.4. They may return as emergent
// quality-test outputs in a later slice.

import 'dart:math' as math;

enum NodeKind { neuron, synapse }

class BrainNode {
  const BrainNode({
    required this.id,
    required this.label,
    required this.kind,
    required this.domain,
    required this.x,
    required this.y,
    required this.z,
    this.haloRadius = 0,
  });

  final String id;
  final String label;
  final NodeKind kind;
  final String domain;
  final double x;
  final double y;
  final double z;
  final double haloRadius;
}

class BrainEdge {
  const BrainEdge({required this.from, required this.to, required this.kind});

  final String from;
  final String to;
  final EdgeKind kind;
}

enum EdgeKind { handler }

class BrainTopology {
  const BrainTopology({required this.nodes, required this.edges});

  final List<BrainNode> nodes;
  final List<BrainEdge> edges;

  static BrainTopology load() {
    return _build();
  }
}

class _DomainAnchor {
  const _DomainAnchor(this.x, this.y, this.z, this.color);
  final double x;
  final double y;
  final double z;
  final int color;
}

const Map<String, _DomainAnchor> _anchors = {
  'kernel': _DomainAnchor(0.0, 1.6, 2.4, 0xE6CFA8),
  'identity': _DomainAnchor(0.0, 0.0, -2.4, 0x9DC3E6),
  'travel': _DomainAnchor(-3.2, 0.6, 0.0, 0xF5B4A0),
  'taxi': _DomainAnchor(3.2, 0.6, 0.0, 0xFFD580),
  'recall': _DomainAnchor(0.0, -0.4, -0.6, 0xC9B5E8),
  'reminders': _DomainAnchor(0.0, 2.2, -1.2, 0xB5E8C9),
  'location': _DomainAnchor(-1.6, -1.6, 1.0, 0xE8B5C5),
  'genesis': _DomainAnchor(0.0, -1.2, -2.6, 0xFFE0A0),
};

int domainColor(String domain) => _anchors[domain]?.color ?? 0xCCCCCC;

({double x, double y, double z}) _spiralOffset(int index, double radius) {
  // Golden-angle 3D spiral keeps clusters visually balanced around their
  // anchor without parameters drifting toward a line.
  const phi = math.pi * (math.sqrt1_2 * 2 + 1);
  final t = index + 0.5;
  final theta = phi * t;
  final y = 1 - (t / 16) * 2;
  final r = math.sqrt(math.max(0, 1 - y * y));
  return (
    x: math.cos(theta) * r * radius,
    y: y * radius * 0.6,
    z: math.sin(theta) * r * radius,
  );
}

BrainNode _placedNeuron(String id, String label, String domain, int order) {
  final a = _anchors[domain]!;
  final off = _spiralOffset(order, 0.85);
  return BrainNode(
    id: id,
    label: label,
    kind: NodeKind.neuron,
    domain: domain,
    x: a.x + off.x,
    y: a.y + off.y,
    z: a.z + off.z,
  );
}

BrainNode _placedSynapse(String id, String label, String domain, int order) {
  final a = _anchors[domain]!;
  final off = _spiralOffset(order + 7, 1.15);
  return BrainNode(
    id: id,
    label: label,
    kind: NodeKind.synapse,
    domain: domain,
    x: a.x + off.x * 1.2,
    y: a.y + off.y * 1.2,
    z: a.z + off.z * 1.2,
  );
}

BrainTopology _build() {
  final nodes = <BrainNode>[
    _placedNeuron('kernel.cortex', 'Cortex', 'kernel', 0),
    _placedNeuron('kernel.discovery', 'Discovery', 'kernel', 1),
    _placedNeuron('kernel.gateway', 'Gateway', 'kernel', 2),
    _placedNeuron('identity.neuron', 'Identity', 'identity', 0),
    _placedNeuron('identity.auth', 'Auth', 'identity', 1),
    _placedNeuron('travel.plan', 'PlanTrip', 'travel', 0),
    _placedNeuron('travel.find_flights', 'FindFlights', 'travel', 1),
    _placedNeuron('travel.find_hotels', 'FindHotels', 'travel', 2),
    _placedNeuron('travel.find_places', 'FindPlaces', 'travel', 3),
    _placedNeuron('travel.flight_search', 'FlightSearch', 'travel', 4),
    _placedNeuron('travel.hotel_search', 'HotelSearch', 'travel', 5),
    _placedNeuron('travel.place_search', 'PlaceSearch', 'travel', 6),
    _placedNeuron('travel.flight_monitor', 'FlightMonitor', 'travel', 7),
    _placedNeuron('taxi.order_ride', 'OrderRideHome', 'taxi', 0),
    _placedNeuron('taxi.ride_request', 'RideRequest', 'taxi', 1),
    _placedNeuron('recall', 'Recall', 'recall', 0),
    _placedNeuron('reminders.neuron', 'Reminders', 'reminders', 0),
    _placedNeuron('reminders.plan', 'SetReminder', 'reminders', 1),
    _placedNeuron('location.neuron', 'Location', 'location', 0),
    _placedNeuron('genesis.creator', 'Creator', 'genesis', 0),
    _placedNeuron('genesis.missed', 'MissedIntentTracker', 'genesis', 1),
    _placedNeuron('genesis.optimizer', 'NeuronOptimizer', 'genesis', 2),
    _placedNeuron('genesis.proposal_log', 'ProposalLog', 'genesis', 3),
    _placedSynapse('syn.chat_intent', 'ChatIntent', 'kernel', 0),
    _placedSynapse('syn.recall_question', 'RecallQuestion', 'recall', 0),
    _placedSynapse('syn.reminder_set', 'ReminderSet', 'reminders', 0),
    _placedSynapse('syn.reminder_due', 'ReminderDue', 'reminders', 1),
    _placedSynapse(
      'syn.find_flights_request',
      'FindFlightsRequest',
      'travel',
      0,
    ),
    _placedSynapse('syn.find_hotels_request', 'FindHotelsRequest', 'travel', 1),
    _placedSynapse('syn.find_places_request', 'FindPlacesRequest', 'travel', 2),
    _placedSynapse('syn.flight_delayed', 'FlightDelayed', 'travel', 3),
    _placedSynapse('syn.ride_request', 'RideRequest', 'taxi', 0),
    _placedSynapse('syn.location_visited', 'LocationVisited', 'location', 0),
    _placedSynapse('syn.proposal_created', 'ProposalCreated', 'genesis', 0),
  ];

  final edges = <BrainEdge>[
    const BrainEdge(
      from: 'syn.chat_intent',
      to: 'kernel.cortex',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.recall_question',
      to: 'recall',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.reminder_set',
      to: 'reminders.neuron',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.reminder_due',
      to: 'reminders.neuron',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.find_flights_request',
      to: 'travel.flight_search',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.find_hotels_request',
      to: 'travel.hotel_search',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.find_places_request',
      to: 'travel.place_search',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.flight_delayed',
      to: 'travel.flight_monitor',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.ride_request',
      to: 'taxi.ride_request',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.location_visited',
      to: 'location.neuron',
      kind: EdgeKind.handler,
    ),
    const BrainEdge(
      from: 'syn.proposal_created',
      to: 'genesis.proposal_log',
      kind: EdgeKind.handler,
    ),
  ];

  return BrainTopology(nodes: nodes, edges: edges);
}

/// Maps Orleans GrainId.ToString() prefixes (lowercased) to topology node ids.
/// Used by BrainStreamService to convert server-side pulses into IDs that the
/// inspector drawer + pulse animator can resolve.
///
/// Orleans derives a grain type name by lowercasing the grain class name
/// (no suffix stripping). These entries correspond 1:1 to the concrete grain
/// classes in ino. Add entries here when new grain classes are introduced.
const Map<String, String> _grainTypeToNodeId = {
  // Kernel
  'cortexneuron': 'kernel.cortex',
  'discovery': 'kernel.discovery',
  'missedintenttracker': 'genesis.missed',
  'proposallog': 'genesis.proposal_log',

  // Travel — neurons
  'tripplannerneuron': 'travel.plan',
  'flightsearchneuron': 'travel.flight_search',
  'hotelsearchneuron': 'travel.hotel_search',
  'placesearchneuron': 'travel.place_search',
  'flightmonitorneuron': 'travel.flight_monitor',

  // Travel — plans
  'plantripplan': 'travel.plan',
  'findflightsplan': 'travel.find_flights',
  'findhotelsplan': 'travel.find_hotels',
  'findplacesplan': 'travel.find_places',

  // Taxi — neuron + plans
  'ridesearchneuron': 'taxi.ride_request',
  'orderridehomeplan': 'taxi.order_ride',
  'findrideplan': 'taxi.ride_request',

  // Recall
  'recallneuron': 'recall',
  'recallplan': 'recall',

  // Reminders — neuron + plans
  'remindersneuron': 'reminders.neuron',
  'setreminderplan': 'reminders.plan',
  'cancelreminderplan': 'reminders.plan',

  // Location
  'locationneuron': 'location.neuron',

  // Genesis
  'creatorneuron': 'genesis.creator',
  'roslynplan': 'genesis.proposal_log',
  'neuronregistry': 'genesis.proposal_log',
};

/// Returns the topology node id for an Orleans grain id string of the form
/// "graintype/key" (e.g. "cortexneuron/0"), or null if the grain type is not
/// on the topology.
String? topologyIdForGrain(String grainIdString) {
  final slash = grainIdString.indexOf('/');
  final type = (slash > 0 ? grainIdString.substring(0, slash) : grainIdString)
      .toLowerCase();
  return _grainTypeToNodeId[type];
}
