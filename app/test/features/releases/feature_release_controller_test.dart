import 'package:digitalbrain_flutter/features/releases/feature_release_controller.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_gateway.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_models.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

import 'feature_release_test_fixtures.dart';

void main() {
  test('load failure is safe and retryable', () async {
    final gateway = _ReleaseGateway(releaseDetails())
      ..loadError = const TransportException(
        TransportErrorCode.unavailable,
        'network internals',
      );
    final controller = FeatureReleaseController(
      featureId: 'feature-a',
      gateway: gateway,
      idFactory: () => 'rollback-a',
    );

    await controller.load();

    expect(controller.status, FeatureReleaseStatus.loadFailed);
    expect(controller.failure?.retryable, isTrue);
    expect(
      controller.failure?.message,
      "We couldn't load this Feature. Try again.",
    );
    gateway.loadError = null;

    await controller.retry();

    expect(controller.status, FeatureReleaseStatus.ready);
    expect(gateway.loadCalls, 2);
  });

  test('rollback retry retains one idempotency request', () async {
    var idsCreated = 0;
    final gateway = _ReleaseGateway(releaseDetails())
      ..rollbackResult = releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      )
      ..failNextRollback = true;
    final controller = FeatureReleaseController(
      featureId: 'feature-a',
      gateway: gateway,
      idFactory: () => 'rollback-${++idsCreated}',
    );
    await controller.load();

    await controller.rollback();

    expect(controller.status, FeatureReleaseStatus.rollbackFailed);
    expect(controller.failure?.retryable, isTrue);
    expect(gateway.rollbackIds, ['rollback-1']);

    await controller.retry();

    expect(controller.status, FeatureReleaseStatus.restored);
    expect(controller.details?.activeVersion.digest, releaseDigest('b'));
    expect(gateway.rollbackIds, ['rollback-1', 'rollback-1']);
    expect(idsCreated, 1);
  });

  test(
    'rollback succeeds only when the exact target source is restored',
    () async {
      final gateway = _ReleaseGateway(releaseDetails())
        ..rollbackResult = releaseDetails(
          activeCharacter: 'b',
          withPrevious: false,
          revision: Int64(13),
          activeSourceContentCharacter: 'c',
        );
      final controller = FeatureReleaseController(
        featureId: 'feature-a',
        gateway: gateway,
        idFactory: () => 'rollback-a',
      );
      await controller.load();

      await controller.rollback();

      expect(controller.status, FeatureReleaseStatus.rollbackFailed);
      expect(controller.failure?.retryable, isFalse);
      expect(controller.failure?.reloadable, isTrue);
      expect(controller.canRollback, isFalse);
      expect(
        controller.failure?.message,
        'The restored Version could not be verified.',
      );
    },
  );

  for (final malformed in <({String name, FeatureReleaseDetails result})>[
    (
      name: 'changed feature identity',
      result: releaseDetails(
        featureId: 'feature-b',
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      ),
    ),
    (
      name: 'changed installation identity',
      result: releaseDetails(
        installationId: 'installation-b',
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      ),
    ),
    (
      name: 'changed origin operation',
      result: releaseDetails(
        operationId: 'operation-b',
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      ),
    ),
    (
      name: 'changed origin conversation',
      result: releaseDetails(
        conversationId: 'conversation-b',
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      ),
    ),
    (
      name: 'changed origin text',
      result: releaseDetails(
        originatingText: 'Research another company',
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      ),
    ),
    (
      name: 'paused restored authority',
      result: releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        paused: true,
        revision: Int64(13),
      ),
    ),
    (
      name: 'stale previous Version',
      result: releaseDetails(
        activeCharacter: 'b',
        previousCharacter: 'c',
        revision: Int64(13),
      ),
    ),
  ]) {
    test('rejects malformed rollback success with ${malformed.name}', () async {
      final current = releaseDetails();
      final gateway = _ReleaseGateway(current)
        ..rollbackResult = malformed.result;
      final controller = FeatureReleaseController(
        featureId: 'feature-a',
        gateway: gateway,
        idFactory: () => 'rollback-a',
      );
      await controller.load();

      await controller.rollback();

      expect(controller.status, FeatureReleaseStatus.rollbackFailed);
      expect(controller.failure?.reloadable, isTrue);
      expect(controller.canRollback, isFalse);
      expect(controller.details, same(current));
    });
  }

  test('rollback rejects a response whose revision did not advance', () async {
    final gateway = _ReleaseGateway(releaseDetails())
      ..rollbackResult = releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(12),
      );
    final controller = FeatureReleaseController(
      featureId: 'feature-a',
      gateway: gateway,
      idFactory: () => 'rollback-a',
    );
    await controller.load();

    await controller.rollback();

    expect(controller.status, FeatureReleaseStatus.rollbackFailed);
    expect(controller.failure?.reloadable, isTrue);
    expect(controller.canRollback, isFalse);
  });

  test('rollback rejects a response that skips a revision', () async {
    final gateway = _ReleaseGateway(releaseDetails())
      ..rollbackResult = releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(14),
      );
    final controller = FeatureReleaseController(
      featureId: 'feature-a',
      gateway: gateway,
      idFactory: () => 'rollback-a',
    );
    await controller.load();

    await controller.rollback();

    expect(controller.status, FeatureReleaseStatus.rollbackFailed);
    expect(controller.failure?.reloadable, isTrue);
    expect(controller.canRollback, isFalse);
  });

  test('paused authority does not offer or execute rollback', () async {
    var idsCreated = 0;
    final gateway = _ReleaseGateway(
      releaseDetails(paused: true, withPrevious: false),
    );
    final controller = FeatureReleaseController(
      featureId: 'feature-a',
      gateway: gateway,
      idFactory: () => 'rollback-${++idsCreated}',
    );
    await controller.load();

    expect(controller.details?.rollbackAvailable, isFalse);
    expect(controller.canRollback, isFalse);

    await controller.rollback();

    expect(gateway.rollbackIds, isEmpty);
    expect(idsCreated, 0);
  });

  test(
    'terminal rollback reloads fresh detail and creates a newly fenced intent',
    () async {
      var idsCreated = 0;
      final gateway = _ReleaseGateway(releaseDetails())
        ..rollbackErrors.add(
          const TransportException(
            TransportErrorCode.aborted,
            'stale lifecycle revision',
          ),
        );
      final controller = FeatureReleaseController(
        featureId: 'feature-a',
        gateway: gateway,
        idFactory: () => 'rollback-${++idsCreated}',
      );
      await controller.load();

      await controller.rollback();

      expect(controller.status, FeatureReleaseStatus.rollbackFailed);
      expect(controller.failure?.retryable, isFalse);
      expect(controller.failure?.reloadable, isTrue);
      expect(controller.canRollback, isFalse);
      expect(gateway.rollbackIds, ['rollback-1']);

      gateway.loadResult = releaseDetails(revision: Int64(20));
      await controller.retry();

      expect(controller.status, FeatureReleaseStatus.ready);
      expect(controller.details?.revision, Int64(20));
      expect(controller.canRollback, isTrue);
      expect(gateway.loadCalls, 2);

      gateway.rollbackResult = releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(21),
      );
      await controller.rollback();

      expect(controller.status, FeatureReleaseStatus.restored);
      expect(gateway.rollbackIds, ['rollback-1', 'rollback-2']);
      expect(idsCreated, 2);
    },
  );
}

class _ReleaseGateway implements FeatureReleaseGateway {
  _ReleaseGateway(this.loadResult);

  FeatureReleaseDetails loadResult;
  FeatureReleaseDetails? rollbackResult;
  Object? loadError;
  bool failNextRollback = false;
  int loadCalls = 0;
  final List<String> rollbackIds = [];
  final List<Object> rollbackErrors = [];

  @override
  Future<FeatureReleaseDetails> loadFeature(
    String featureId, {
    String? expectedActiveDigest,
  }) async {
    loadCalls++;
    if (loadError case final error?) throw error;
    return loadResult;
  }

  @override
  Future<FeatureReleaseDetails> rollbackFeature({
    required FeatureReleaseDetails current,
    required String idempotencyId,
  }) async {
    rollbackIds.add(idempotencyId);
    if (rollbackErrors.isNotEmpty) throw rollbackErrors.removeAt(0);
    if (failNextRollback) {
      failNextRollback = false;
      throw const TransportException(
        TransportErrorCode.unavailable,
        'network internals',
      );
    }
    return rollbackResult!;
  }
}
