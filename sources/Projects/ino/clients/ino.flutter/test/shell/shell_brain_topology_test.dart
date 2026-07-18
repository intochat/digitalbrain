import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/shell/shell_brain_topology.dart';

void main() {
  test('ShellTopology defines exactly 8 clusters', () {
    expect(ShellTopology.clusters, hasLength(8));
  });

  test('cluster ids match the prototype set', () {
    final ids = ShellTopology.clusters.map((c) => c.id).toSet();
    expect(ids, {
      'cortex', 'travel', 'recall', 'location',
      'reminders', 'taxi', 'genesis', 'identity',
    });
  });

  test('total neurons across clusters equal 39 (prototype invariant)', () {
    final total =
        ShellTopology.clusters.fold<int>(0, (a, c) => a + c.aliases.length);
    expect(total, 39);
  });

  test('aliasLookup resolves PlanTrip to travel', () {
    expect(ShellTopology.aliasLookup('PlanTrip')?.cluster, 'travel');
  });

  test('aliasLookup resolves Cortex to cortex', () {
    expect(ShellTopology.aliasLookup('Cortex')?.cluster, 'cortex');
  });

  test('aliasLookup returns null for unknown alias', () {
    expect(ShellTopology.aliasLookup('Nope'), isNull);
  });

  test('filamentPairs contains the cortex-travel link', () {
    expect(ShellTopology.filamentPairs, contains(('cortex', 'travel')));
  });

  test('every neuron has a non-empty id and matching cluster', () {
    for (final n in ShellTopology.neurons) {
      expect(n.id, isNotEmpty);
      expect(
        ShellTopology.clusters.any((c) => c.id == n.cluster),
        isTrue,
        reason: 'neuron ${n.alias} cluster=${n.cluster} not in clusters list',
      );
    }
  });
}
