import 'package:flutter/widgets.dart';
import 'package:digitalbrain_flutter/features/live/graph/cluster_layout.dart';

class SynapseStreamFeed extends ChangeNotifier {
  final List<GraphEdge> _edges = [];

  void publish(List<GraphEdge> next) {
    _edges
      ..clear()
      ..addAll(next);
    notifyListeners();
  }

  Iterable<GraphEdge> forCorrelation(String cid) =>
      _edges.where((e) => e.correlationId == cid);
}

class SynapseStreamScope extends InheritedNotifier<SynapseStreamFeed> {
  const SynapseStreamScope({
    required super.notifier,
    required super.child,
    super.key,
  });

  static SynapseStreamFeed? maybeOf(BuildContext c) =>
      c.dependOnInheritedWidgetOfExactType<SynapseStreamScope>()?.notifier;
}
