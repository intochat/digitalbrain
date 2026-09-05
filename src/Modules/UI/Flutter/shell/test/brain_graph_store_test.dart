import 'dart:async';
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/chat/brain_graph_store.dart';
import 'package:flutter_test/flutter_test.dart';

BrainSnapshot observation({int deliveries = 2, bool bound = true}) =>
    BrainSnapshot(
      rootId: 'chat:main',
      observedAt: DateTime.utc(2026, 9, 5),
      synapses: bound
          ? [
              BrainSynapse(
                id: 'edge',
                sourceId: 'timer:review',
                targetId: 'behavior:review',
                signalType: 'Tick',
                kind: 'Bound',
                fireCount: deliveries,
              ),
            ]
          : [],
    );

void main() {
  test(
    'initial history is quiet; only a later increased delivery count activates an edge',
    () async {
      var next = observation();
      final store = BrainGraphStore(
        read: () async => next,
        interval: const Duration(days: 1),
      );
      addTearDown(store.dispose);
      await Future<void>.delayed(Duration.zero);
      expect(store.activeEdges, isEmpty);
      next = observation(deliveries: 3);
      await store.refresh();
      expect(store.activeEdges, {'edge'});
      await store.refresh();
      expect(store.activeEdges, isEmpty);
    },
  );

  test(
    'a failed poll keeps the observation and exposes its stale state',
    () async {
      var fail = false;
      final first = observation();
      final store = BrainGraphStore(
        read: () async {
          if (fail) throw StateError('offline');
          return first;
        },
        interval: const Duration(days: 1),
      );
      addTearDown(store.dispose);
      await Future<void>.delayed(Duration.zero);
      fail = true;
      await store.refresh();
      expect(store.snapshot, same(first));
      expect(store.failure, isNotNull);
      expect(store.stale, isTrue);
      expect(store.activeNodes, isEmpty);
      expect(store.activeEdges, isEmpty);
    },
  );

  test(
    'unsubscribe remains pending until mutation and fresh observation agree',
    () async {
      var next = observation();
      final accepted = Completer<void>();
      final store = BrainGraphStore(
        read: () async => next,
        interval: const Duration(days: 1),
        setSubscription:
            ({
              required sourceId,
              required targetId,
              required signalType,
              required subscribed,
            }) async {
              expect(subscribed, isFalse);
              await accepted.future;
              next = observation(bound: false);
            },
      );
      addTearDown(store.dispose);
      await Future<void>.delayed(Duration.zero);
      final result = store.subscribe(
        sourceId: 'timer:review',
        targetId: 'behavior:review',
        signalType: 'Tick',
        subscribed: false,
      );
      expect(store.mutating, isTrue);
      expect(store.snapshot!.synapses, hasLength(1));
      accepted.complete();
      expect(await result, isTrue);
      expect(store.snapshot!.synapses, isEmpty);
      expect(store.mutating, isFalse);
    },
  );

  test(
    'an unconfirmed mutation reports failure instead of removing an edge optimistically',
    () async {
      final store = BrainGraphStore(
        read: () async => observation(),
        interval: const Duration(days: 1),
        setSubscription:
            ({
              required sourceId,
              required targetId,
              required signalType,
              required subscribed,
            }) async {},
      );
      addTearDown(store.dispose);
      await Future<void>.delayed(Duration.zero);
      expect(
        await store.subscribe(
          sourceId: 'timer:review',
          targetId: 'behavior:review',
          signalType: 'Tick',
          subscribed: false,
        ),
        isFalse,
      );
      expect(store.snapshot!.synapses, hasLength(1));
      expect(store.failure, contains('not been confirmed'));
    },
  );

  test('disposal ignores an in-flight result and never notifies', () async {
    final pending = Completer<BrainSnapshot>();
    final store = BrainGraphStore(read: () => pending.future);
    var notifications = 0;
    store.addListener(() => notifications++);
    store.dispose();
    pending.complete(observation());
    await Future<void>.delayed(Duration.zero);
    expect(notifications, 0);
    expect(store.snapshot, isNull);
  });

  test('newly reachable neurons do not replay old retained activity', () async {
    final firstTime = DateTime.utc(2026, 9, 5, 12);
    var next = BrainSnapshot(rootId: 'chat:main', observedAt: firstTime);
    final store = BrainGraphStore(
      read: () async => next,
      interval: const Duration(days: 1),
    );
    addTearDown(store.dispose);
    await Future<void>.delayed(Duration.zero);
    next = BrainSnapshot(
      rootId: 'chat:main',
      observedAt: firstTime.add(const Duration(seconds: 2)),
      nodes: const [
        BrainNeuron(
          id: 'new',
          type: 'Documents',
          name: 'new',
          label: 'Documents',
          module: 'Files',
          incomingSequence: 2,
        ),
      ],
      activity: [
        BrainActivity(
          id: 'old',
          neuronId: 'new',
          direction: 'Incoming',
          sequence: 1,
          signalType: 'Read',
          timestamp: firstTime.subtract(const Duration(hours: 1)),
        ),
      ],
    );
    await store.refresh();
    expect(store.activeNodes, isEmpty);
    next = BrainSnapshot(
      rootId: 'chat:main',
      observedAt: firstTime.add(const Duration(seconds: 4)),
      nodes: const [
        BrainNeuron(
          id: 'new',
          type: 'Documents',
          name: 'new',
          label: 'Documents',
          module: 'Files',
          incomingSequence: 3,
        ),
      ],
      activity: [
        BrainActivity(
          id: 'fresh',
          neuronId: 'new',
          direction: 'Incoming',
          sequence: 3,
          signalType: 'Read',
          timestamp: firstTime.add(const Duration(seconds: 3)),
        ),
      ],
    );
    await store.refresh();
    expect(store.activeNodes, {'new'});
  });
}
