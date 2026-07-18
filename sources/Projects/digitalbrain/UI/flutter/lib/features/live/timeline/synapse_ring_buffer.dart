import 'package:flutter/foundation.dart';

import '../graph/cluster_layout.dart';

class SynapseRingBuffer extends ChangeNotifier {
  SynapseRingBuffer({int capacity = 200}) : _capacity = capacity;

  int _capacity;
  int get capacity => _capacity;
  final List<GraphEdge> _items = [];

  List<GraphEdge> get items => List.unmodifiable(_items);
  int get length => _items.length;

  void add(GraphEdge edge) {
    _items.add(edge);
    while (_items.length > _capacity) {
      _items.removeAt(0);
    }
    notifyListeners();
  }

  void setCapacity(int newCapacity) {
    if (newCapacity == _capacity) return;
    _capacity = newCapacity;
    while (_items.length > _capacity) {
      _items.removeAt(0);
    }
    notifyListeners();
  }

  GraphEdge? at(int index) =>
      (index < 0 || index >= _items.length) ? null : _items[index];
}
