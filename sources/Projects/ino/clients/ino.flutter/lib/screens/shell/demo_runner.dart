import 'dart:async';

import 'package:flutter/material.dart';

import '../../persona/persona_state.dart';
import '../../state/persona_bloc.dart';
import 'shell_brain_canvas.dart';
import 'shell_brain_topology.dart';
import 'shell_compose.dart';
import 'storyboard.dart';
import 'storyboard_cards.dart';

/// References the live widget state objects that DemoRunner dispatches into.
/// Constructed by _ShellScreenState and passed to DemoRunner on creation (T10.2).
class ShellRefs {
  const ShellRefs({
    required this.canvasKey,
    required this.composeKey,
    required this.persona,
    this.input,
  });

  final GlobalKey<ShellBrainCanvasState> canvasKey;
  final GlobalKey<ShellComposeState> composeKey;
  final PersonaBloc persona;
  final TextEditingController? input;
}

/// Reads a [Storyboard] and replays its events by scheduling [Timer]s that
/// dispatch into [PersonaBloc], [ShellBrainCanvasState], and [ShellComposeState].
///
/// Lifecycle:
///   play()          — loads asset by id, schedules all timers, clears prior run
///   playFromJson()  — same but accepts raw JSON string (used by tests)
///   replan()        — loads the replan storyboard without clearing existing cards
///   stop()          — cancels all pending timers, resets persona to idle
///   togglePause()   — gates dispatch for timers still pending (timers keep ticking)
///   replayTrace()   — re-fires the synapse arc for a card id (T10.2 chevron tap)
///   fireTest()      — spawns a single comet from an inspected alias (T10.2 inspector)
class DemoRunner {
  DemoRunner({required this.refs, Storyboard Function(String json)? parse})
      : _parse = parse ?? Storyboard.parse;

  final ShellRefs refs;
  final Storyboard Function(String json) _parse;

  final List<Timer> _timers = [];
  bool _paused = false;

  /// Loads the storyboard with [id] from the asset bundle and plays it.
  Future<void> play({String id = 'tokyo'}) async {
    stop();
    final sb = await Storyboard.loadFromAsset(id);
    _scheduleAll(sb);
  }

  /// Parses [json] directly and plays it. Avoids rootBundle — used in tests.
  void playFromJson(String json) {
    stop();
    final sb = _parse(json);
    _scheduleAll(sb);
  }

  /// Plays the replan scenario without clearing existing cards.
  Future<void> replan() async {
    final sb = await Storyboard.loadFromAsset('tokyo-replan');
    _scheduleAll(sb);
  }

  /// Cancels all pending timers and resets the persona to idle.
  void stop() {
    for (final t in _timers) {
      t.cancel();
    }
    _timers.clear();
    _paused = false;
    refs.persona.add(PersonaEmotionChanged(PersonaEmotion.idle));
  }

  /// Toggles paused. While paused, dispatching is suppressed for events whose
  /// timers have not yet fired. Timers already scheduled continue to elapse so
  /// that unpausing does not replay elapsed events.
  void togglePause() {
    _paused = !_paused;
  }

  /// Re-fires the synapse arcs that led to [cardId], staggered by 280 ms each.
  /// Used by T10.2 to wire the chevron button on each ShellCard.
  void replayTrace(String cardId) {
    final arcs = _arcsFor(cardId);
    for (var i = 0; i < arcs.length; i++) {
      final (from, to, gold) = arcs[i];
      _timers.add(Timer(Duration(milliseconds: i * 280), () {
        refs.canvasKey.currentState?.spawnSynapse(
          from: from,
          to: to,
          payload: const {},
          gold: gold,
          duration: 0.5,
        );
      }));
    }
  }

  /// Spawns a self-test comet from [alias] to its canonical partner neuron.
  /// Used by T10.2 to wire the inspector fire-test button.
  void fireTest(String alias) {
    final partner = alias == 'Cortex' ? 'PlanTrip' : 'Cortex';
    refs.canvasKey.currentState?.spawnSynapse(
      from: alias,
      to: partner,
      payload: const {'test': true},
      gold: false,
      duration: 0.5,
    );
  }

  void _scheduleAll(Storyboard sb) {
    for (final ev in sb.events) {
      final ms = (ev.t * 1000).round();
      _timers.add(Timer(Duration(milliseconds: ms), () => _dispatch(ev)));
    }
  }

  void _dispatch(StoryboardEvent ev) {
    if (_paused) return;
    switch (ev) {
      case OrbEvent(:final state):
        refs.persona.add(PersonaEmotionChanged(_mapOrb(state)));
      case UtterEvent(:final text):
        refs.input?.text = text;
      case SynapseEvent(:final from, :final to, :final payload, :final gold):
        refs.canvasKey.currentState?.spawnSynapse(
          from: from,
          to: to,
          payload: payload,
          gold: gold,
        );
        final target = ShellTopology.aliasLookup(to);
        if (target != null) {
          refs.canvasKey.currentState?.focusOnCluster(target.cluster);
        }
      case CardEvent(:final id, :final stage, :final fromCluster):
        if (stage == 'enter') {
          final model = StoryboardCards.resolve(id);
          if (model != null) {
            final origin = _projectClusterCenter(fromCluster);
            refs.composeKey.currentState?.showCard(
              model: model,
              originScreenOffset: origin ?? Offset.zero,
            );
          }
        } else if (stage == 'morph' && id == 'hotels') {
          refs.composeKey.currentState
              ?.morphCard(id, StoryboardCards.hotelsReplan);
        }
    }
  }

  PersonaEmotion _mapOrb(String state) => switch (state) {
        'listening' => PersonaEmotion.listening,
        'thinking' => PersonaEmotion.thinking,
        'speaking' || 'responding' => PersonaEmotion.responding,
        'celebrating' => PersonaEmotion.celebrating,
        'confused' => PersonaEmotion.confused,
        _ => PersonaEmotion.idle,
      };

  Offset? _projectClusterCenter(String? clusterId) {
    if (clusterId == null) return null;
    final canvas = refs.canvasKey.currentState;
    if (canvas == null) return null;

    ShellCluster? cluster;
    for (final c in ShellTopology.clusters) {
      if (c.id == clusterId) {
        cluster = c;
        break;
      }
    }
    if (cluster == null) return null;

    final result = canvas.projectVec3WithDepth(
      cluster.position.x * 2.0,
      cluster.position.y * 2.0,
      cluster.position.z * 2.0,
    );
    return result?.offset;
  }

  static List<(String from, String to, bool gold)> _arcsFor(String cardId) =>
      switch (cardId) {
        'flights' => const [
            ('Cortex', 'PlanTrip', false),
            ('PlanTrip', 'FindFlights', false),
          ],
        'hotels' => const [
            ('Cortex', 'PlanTrip', false),
            ('Preferences', 'PlanTrip', true),
            ('PlanTrip', 'FindHotels', false),
          ],
        'itinerary' => const [
            ('Forecast', 'PlanTrip', false),
            ('Preferences', 'PlanTrip', true),
            ('PlanTrip', 'FindPlaces', false),
          ],
        'reminder' => const [
            ('PlanTrip', 'VisaReminder', false),
          ],
        _ => const [],
      };
}
