import 'dart:convert';

import 'package:flutter/services.dart' show rootBundle;

sealed class StoryboardEvent {
  const StoryboardEvent({required this.t});
  final double t;
}

class OrbEvent extends StoryboardEvent {
  const OrbEvent({required super.t, required this.state});
  // listening / thinking / speaking / responding / celebrating / confused / idle
  final String state;
}

class UtterEvent extends StoryboardEvent {
  const UtterEvent({required super.t, required this.text});
  final String text;
}

class SynapseEvent extends StoryboardEvent {
  const SynapseEvent({
    required super.t,
    required this.from,
    required this.to,
    required this.payload,
    required this.gold,
  });
  final String from;
  final String to;
  final Map<String, dynamic> payload;
  final bool gold;
}

class CardEvent extends StoryboardEvent {
  const CardEvent({
    required super.t,
    required this.id,
    required this.stage,
    required this.fromCluster,
  });
  final String id;
  final String stage; // 'enter' | 'morph'
  final String? fromCluster;
}

class Storyboard {
  const Storyboard({
    required this.id,
    required this.label,
    required this.durationSeconds,
    required this.events,
  });

  final String id;
  final String label;
  final double durationSeconds;
  final List<StoryboardEvent> events;

  static Future<Storyboard> loadFromAsset(String id) async {
    final raw = await rootBundle.loadString('assets/storyboards/$id.json');
    return parse(raw);
  }

  static Storyboard parse(String json) {
    final root = jsonDecode(json) as Map<String, dynamic>;
    final events = (root['events'] as List)
        .map((e) => _parseEvent(e as Map<String, dynamic>))
        .toList(growable: false);
    return Storyboard(
      id: root['id'] as String,
      label: root['label'] as String,
      durationSeconds: (root['duration_s'] as num).toDouble(),
      events: events,
    );
  }

  static StoryboardEvent _parseEvent(Map<String, dynamic> e) {
    final t = (e['t'] as num).toDouble();
    return switch (e['kind']) {
      'orb' => OrbEvent(t: t, state: e['state'] as String),
      'utter' => UtterEvent(t: t, text: e['text'] as String),
      'syn' => SynapseEvent(
          t: t,
          from: e['from'] as String,
          to: e['to'] as String,
          payload: Map<String, dynamic>.from(e['payload'] as Map),
          gold: (e['gold'] as bool?) ?? false,
        ),
      'card' => CardEvent(
          t: t,
          id: e['id'] as String,
          stage: e['stage'] as String,
          fromCluster: e['from'] as String?,
        ),
      _ => throw FormatException('Unknown storyboard event kind: ${e['kind']}'),
    };
  }
}
