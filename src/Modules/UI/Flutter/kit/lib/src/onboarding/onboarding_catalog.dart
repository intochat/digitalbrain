import 'package:flutter/material.dart';

import '../components/graph/graph_models.dart';
import 'onboarding_models.dart';

abstract final class OnboardingCatalog {
  static const elon = GraphNode(id: 'elon', label: 'elon');
  static const alice = GraphNode(id: 'alice', label: 'alice');
  static const vlad = GraphNode(id: 'vlad', label: 'vlad');
  static const bob = GraphNode(id: 'bob', label: 'bob');
  static const timeline = GraphNode(
    id: 'timeline',
    label: 'unsubscribed',
    dimmed: true,
  );
  static const profile = GraphNode(
    id: 'profile',
    label: 'profile',
    kind: GraphNodeKind.entity,
  );
  static const timeModule = GraphNode(
    id: 'time-module',
    label: 'Time',
    kind: GraphNodeKind.module,
  );
  static const timer = GraphNode(id: 'timer', label: 'Timer', cluster: 'Time');

  static const elonToAlice = GraphEdge(
    id: 'elon-alice',
    sourceId: 'elon',
    targetId: 'alice',
  );
  static const vladToBob = GraphEdge(
    id: 'vlad-bob',
    sourceId: 'vlad',
    targetId: 'bob',
  );
  static const clientToTimer = GraphEdge(
    id: 'client-timer',
    sourceId: 'client',
    targetId: 'timer',
  );

  static const client = GraphNode(id: 'client', label: 'script');

  static List<OnboardingCapability> get capabilities => [
    fire,
    handle,
    synapse,
    broadcast,
    subscribe,
    journal,
    entity,
    module,
  ];

  static OnboardingCapability byId(String id) =>
      capabilities.firstWhere((capability) => capability.id == id);

  static const fire = OnboardingCapability(
    id: 'fire',
    title: 'Fire',
    blurb: 'Send a signal at a named neuron.',
    rule:
        'A script or assistant fires SendAsync at a named neuron. The payload is the Signal. The envelope (id, correlation, caller, sequence) rides with it. This is not a broadcast.',
    icon: Icons.flash_on_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [],
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'fire-1'),
      ),
    ],
  );

  static const handle = OnboardingCapability(
    id: 'handle',
    title: 'Handle',
    blurb: 'IHandle is a type capability, not a subscription.',
    rule:
        'IHandle<NewPost> means this grain type can receive NewPost. It does not subscribe Alice to every account. Unhandled fires still journal; they do not learn a route.',
    icon: Icons.pan_tool_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [],
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'handle-1'),
      ),
    ],
  );

  static const synapse = OnboardingCapability(
    id: 'synapse',
    title: 'Synapse',
    blurb: 'A handled fire writes an edge on the source.',
    rule:
        'When Alice handles Elon’s NewPost, a learned synapse is stored on Elon: elon --NewPost--> alice. Unhandled fire: journal only, no edge.',
    icon: Icons.hub_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [],
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'syn-1'),
      ),
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [elonToAlice],
        highlightEdgeId: 'elon-alice',
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'syn-2'),
      ),
    ],
  );

  static const broadcast = OnboardingCapability(
    id: 'broadcast',
    title: 'Broadcast',
    blurb: 'Fan-out from this neuron’s audience.',
    rule:
        'Broadcast follows this source neuron’s existing synapses for the signal type. An unsubscribed handler receives nothing. The emitter never receives its own broadcast.',
    icon: Icons.campaign_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice, timeline],
        edges: [elonToAlice],
        duration: Duration(milliseconds: 500),
      ),
      OnboardingLessonFrame(
        nodes: [elon, alice, timeline],
        edges: [elonToAlice],
        pulse: GraphPulse(
          fromId: 'elon',
          toId: 'alice',
          signature: 'broadcast-alice',
        ),
        highlightEdgeId: 'elon-alice',
      ),
      OnboardingLessonFrame(
        nodes: [elon, alice, timeline],
        edges: [elonToAlice],
      ),
    ],
  );

  static const subscribe = OnboardingCapability(
    id: 'subscribe',
    title: 'Subscribe',
    blurb: 'Follow Elon is not follow Vlad.',
    rule:
        'Alice’s subscription is a NewPost synapse on Elon. Bob’s is on Vlad. Elon’s broadcast pulses Alice only; Bob stays dimmed. Azure queues are not how you follow a named account.',
    icon: Icons.notifications_active_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice, vlad, bob],
        edges: [elonToAlice, vladToBob],
        duration: Duration(milliseconds: 700),
      ),
      OnboardingLessonFrame(
        nodes: [
          elon,
          alice,
          vlad,
          GraphNode(id: 'bob', label: 'bob', dimmed: true),
        ],
        edges: [elonToAlice, vladToBob],
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'sub-elon'),
        highlightEdgeId: 'elon-alice',
      ),
    ],
  );

  static const journal = OnboardingCapability(
    id: 'journal',
    title: 'Journal',
    blurb: 'Interactions, bounded — not the snapshot.',
    rule:
        'The traffic journal is a bounded window of SignalDelivery envelopes. It is not the chart, the bio, or the synapse map. Past the window you get a reset snapshot, not infinite history.',
    icon: Icons.receipt_long_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice],
        edges: [elonToAlice],
        pulse: GraphPulse(
          fromId: 'elon',
          toId: 'alice',
          signature: 'journal-1',
        ),
        highlightEdgeId: 'elon-alice',
      ),
    ],
  );

  static const entity = OnboardingCapability(
    id: 'entity',
    title: 'Entity',
    blurb: 'Snapshots. Not graph endpoints.',
    rule:
        'Profile, charts, and sheets are entities: current values, no journal, no synapses. Neurons fire and journal; entities persist points. The square is never a pulse target.',
    icon: Icons.inventory_2_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [elon, alice, profile],
        edges: [elonToAlice],
        duration: Duration(milliseconds: 500),
      ),
      OnboardingLessonFrame(
        nodes: [
          elon,
          alice,
          GraphNode(
            id: 'profile',
            label: 'profile',
            kind: GraphNodeKind.entity,
            dimmed: true,
          ),
        ],
        edges: [elonToAlice],
        pulse: GraphPulse(fromId: 'elon', toId: 'alice', signature: 'entity-1'),
      ),
    ],
  );

  static const module = OnboardingCapability(
    id: 'module',
    title: 'Module',
    blurb: 'Modules contain neurons with related responsibilities.',
    rule:
        'Time groups timer neurons; other modules group their own neurons and tools. A script sends StartTimer to a named Timer neuron. The module groups the neurons; it does not receive the signal.',
    icon: Icons.extension_outlined,
    frames: [
      OnboardingLessonFrame(
        nodes: [client, timeModule, timer],
        edges: [],
        duration: Duration(milliseconds: 400),
      ),
      OnboardingLessonFrame(
        nodes: [client, timeModule, timer],
        edges: [clientToTimer],
        pulse: GraphPulse(
          fromId: 'client',
          toId: 'timer',
          signature: 'module-1',
        ),
      ),
    ],
  );
}
