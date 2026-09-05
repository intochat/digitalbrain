import 'dart:async';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:flutter/foundation.dart';

/// Keeps authoritative snapshots without overlapping polls or replaying history
/// as live traffic. A failed read preserves the last observation, visibly stale.
final class BrainGraphStore extends ChangeNotifier {
  BrainGraphStore({
    required this.read,
    this.setSubscription,
    this.interval = const Duration(seconds: 2),
  }) {
    unawaited(refresh());
  }
  final ReadBrain? read;
  final SetBrainSubscription? setSubscription;
  final Duration interval;
  BrainSnapshot? snapshot;
  String? failure;
  bool loading = false, mutating = false;
  bool stale = false;
  Set<String> activeNodes = {}, activeEdges = {};
  int revision = 0;
  bool _disposed = false;
  Timer? _timer;

  Future<void> refresh() async {
    if (_disposed || loading || read == null) return;
    _timer?.cancel();
    loading = true;
    try {
      final next = await read!().timeout(const Duration(seconds: 12));
      if (_disposed) return;
      final previous = snapshot;
      activeNodes = {};
      activeEdges = {};
      if (previous != null) {
        final events = previous.activity.map((e) => e.id).toSet();
        final knownNodes = {for (final node in previous.nodes) node.id: node};
        activeNodes = next.activity
            .where((event) {
              if (events.contains(event.id)) return false;
              final known = knownNodes[event.neuronId];
              if (known == null) {
                // Newly reachable neurons can bring an entire retained journal.
                // Only entries later than our prior observation are fresh work.
                return event.timestamp.isAfter(previous.observedAt);
              }
              final cursor = event.direction.toLowerCase() == 'incoming'
                  ? known.incomingSequence
                  : known.outgoingSequence;
              return event.sequence > cursor;
            })
            .map((e) => e.neuronId)
            .toSet();
        final counts = {for (final e in previous.synapses) e.id: e.fireCount};
        activeEdges = next.synapses
            .where(
              (e) => counts.containsKey(e.id) && e.fireCount > counts[e.id]!,
            )
            .map((e) => e.id)
            .toSet();
      }
      snapshot = next;
      failure = null;
      stale = false;
      revision++;
    } catch (_) {
      if (!_disposed) {
        failure = 'Cannot refresh the brain. Showing the last observation.';
        stale = true;
        activeNodes = {};
        activeEdges = {};
      }
    } finally {
      loading = false;
      if (!_disposed) {
        notifyListeners();
        _timer = Timer(interval, refresh);
      }
    }
  }

  Future<bool> subscribe({
    required String sourceId,
    required String targetId,
    required String signalType,
    required bool subscribed,
  }) async {
    if (_disposed || mutating || read == null || setSubscription == null) {
      return false;
    }
    mutating = true;
    notifyListeners();
    try {
      await setSubscription!(
        sourceId: sourceId,
        targetId: targetId,
        signalType: signalType,
        subscribed: subscribed,
      ).timeout(const Duration(seconds: 15));
      if (_disposed) return false;
      // A read already in flight may predate the mutation. Wait until it has
      // finished, then obtain a fresh authoritative snapshot.
      while (loading && !_disposed) {
        await Future<void>.delayed(const Duration(milliseconds: 50));
      }
      if (_disposed) return false;
      await refresh();
      if (failure != null) return false;
      final present =
          snapshot?.synapses.any(
            (edge) =>
                edge.sourceId == sourceId &&
                edge.targetId == targetId &&
                edge.signalType == signalType &&
                (!subscribed || edge.kind == 'Bound'),
          ) ??
          false;
      if (present != subscribed) {
        failure =
            'The subscription has not been confirmed. Refresh and try again.';
        return false;
      }
      return true;
    } catch (_) {
      if (!_disposed) {
        failure =
            'The subscription could not be changed. Refresh and try again.';
      }
      return false;
    } finally {
      mutating = false;
      if (!_disposed) notifyListeners();
    }
  }

  @override
  void dispose() {
    _disposed = true;
    _timer?.cancel();
    super.dispose();
  }
}
