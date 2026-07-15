import 'package:digitalbrain_flutter/features/releases/feature_release_gateway.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_models.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_page.dart';
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'feature_release_test_fixtures.dart';

void main() {
  testWidgets('loads and presents the installed Feature authority', (
    tester,
  ) async {
    final gateway = _PageGateway(releaseDetails());

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    expect(gateway.loadedFeatureIds, ['feature-a']);
    expect(find.text('Feature Version'), findsOneWidget);
    expect(find.text('Feature release'), findsNothing);
    expect(find.text('Active Version'), findsOneWidget);
    expect(find.text(releaseDigest('a')), findsOneWidget);
    expect(find.text('Runtime-authored source'), findsOneWidget);
    expect(find.text(sourceReference('a')), findsOneWidget);
    expect(find.text('Research Acme'), findsOneWidget);
    expect(
      find.text('digitalbrain.integration.email.read · v1'),
      findsOneWidget,
    );
    expect(find.text('Google · connection-acme'), findsOneWidget);
    expect(find.text('Built-in · No connection'), findsOneWidget);
    expect(find.textContaining('No Connector'), findsNothing);
    expect(
      find.text(
        'Only digitalbrain.integration.email.read; input limit must equal 25; '
        'input mailbox must equal "inbox"',
      ),
      findsOneWidget,
    );
    expect(find.text('manual'), findsOneWidget);
    expect(find.text('schedule:weekday'), findsOneWidget);
    expect(find.text('Active'), findsOneWidget);
    expect(find.text('Running'), findsNothing);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('binds a deep-linked Version to the rendered authority', (
    tester,
  ) async {
    final gateway = _PageGateway(releaseDetails());

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(
          featureId: 'feature-a',
          expectedReleaseDigest: releaseDigest('a'),
          gateway: gateway,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(gateway.expectedActiveDigests, [releaseDigest('a')]);
    expect(find.text(releaseDigest('a')), findsOneWidget);
    expect(find.text('Active Version'), findsOneWidget);
  });

  testWidgets('does not render authority for a different deep-linked Version', (
    tester,
  ) async {
    final gateway = _PageGateway(
      releaseDetails(activeCharacter: 'b', withPrevious: false),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(
          featureId: 'feature-a',
          expectedReleaseDigest: releaseDigest('a'),
          gateway: gateway,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(gateway.expectedActiveDigests, [releaseDigest('a')]);
    expect(
      find.text('The Feature response could not be verified.'),
      findsOneWidget,
    );
    expect(find.text('Active Version'), findsNothing);
    expect(find.text(releaseDigest('b')), findsNothing);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsNothing);
  });

  testWidgets('uses product language when no access is required', (
    tester,
  ) async {
    final gateway = _PageGateway(_releaseDetailsWithoutAccess());

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No access is required.'), findsOneWidget);
    expect(find.textContaining('capabilit'), findsNothing);
  });

  testWidgets('offers a retry after a safe load failure', (tester) async {
    final gateway = _PageGateway(releaseDetails())
      ..loadErrors.add(
        const TransportException(
          TransportErrorCode.unavailable,
          'network internals',
        ),
      );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.text("We couldn't load this Feature. Try again."),
      findsOneWidget,
    );
    await tester.tap(find.byKey(featureReleaseRetryButtonKey));
    await tester.pumpAndSettle();

    expect(gateway.loadedFeatureIds, ['feature-a', 'feature-a']);
    expect(find.text('Active Version'), findsOneWidget);
  });

  testWidgets('requires confirmation and announces exact restoration', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    final gateway = _PageGateway(releaseDetails())
      ..rollbackResult = releaseDetails(
        activeCharacter: 'b',
        withPrevious: false,
        revision: Int64(13),
      );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    final rollback = find.byKey(featureReleaseRollbackButtonKey);
    await tester.ensureVisible(rollback);
    await tester.tap(rollback);
    await tester.pumpAndSettle();
    expect(find.text('Roll back to the previous Version?'), findsOneWidget);
    expect(find.text(releaseDigest('b')), findsOneWidget);
    await tester.tap(find.byKey(featureReleaseCancelRollbackButtonKey));
    await tester.pumpAndSettle();
    expect(gateway.rollbackIds, isEmpty);

    await tester.ensureVisible(rollback);
    await tester.tap(rollback);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(featureReleaseConfirmRollbackButtonKey));
    await tester.pumpAndSettle();

    expect(gateway.rollbackIds, hasLength(1));
    expect(find.text('Previous Version restored exactly'), findsOneWidget);
    expect(
      find.byWidgetPredicate(
        (widget) =>
            widget is Semantics &&
            widget.properties.liveRegion == true &&
            widget.properties.label == 'Previous Version restored exactly',
        skipOffstage: false,
      ),
      findsOneWidget,
    );
    expect(find.text(releaseDigest('b')), findsOneWidget);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsNothing);
    semantics.dispose();
  });

  testWidgets('does not offer rollback without a previous Version', (
    tester,
  ) async {
    final gateway = _PageGateway(releaseDetails(withPrevious: false));

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('No previous Version is available.'), findsOneWidget);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsNothing);
  });

  testWidgets('shows the paused reason', (tester) async {
    final gateway = _PageGateway(
      releaseDetails(paused: true, withPrevious: false),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Paused'), findsOneWidget);
    expect(find.text('Connection was revoked'), findsOneWidget);
    expect(find.byKey(featureReleaseRollbackButtonKey), findsNothing);
  });

  testWidgets('terminal rollback disables stale action and reloads detail', (
    tester,
  ) async {
    final gateway = _PageGateway(releaseDetails())
      ..rollbackErrors.add(
        const TransportException(
          TransportErrorCode.failedPrecondition,
          'stale lifecycle revision',
        ),
      );

    await tester.pumpWidget(
      MaterialApp(
        home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
      ),
    );
    await tester.pumpAndSettle();

    final rollback = find.byKey(featureReleaseRollbackButtonKey);
    await tester.ensureVisible(rollback);
    await tester.tap(rollback);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(featureReleaseConfirmRollbackButtonKey));
    await tester.pumpAndSettle();

    expect(
      find.text('This rollback is no longer valid. Reload the Feature.'),
      findsOneWidget,
    );
    expect(find.byKey(featureReleaseReloadButtonKey), findsOneWidget);
    expect(tester.widget<FilledButton>(rollback).onPressed, isNull);

    gateway.loadResult = releaseDetails(revision: Int64(20));
    final reload = find.byKey(featureReleaseReloadButtonKey);
    await tester.ensureVisible(reload);
    await tester.tap(reload);
    await tester.pumpAndSettle();

    expect(gateway.loadedFeatureIds, ['feature-a', 'feature-a']);
    expect(find.byKey(featureReleaseReloadButtonKey), findsNothing);
    expect(
      tester
          .widget<FilledButton>(find.byKey(featureReleaseRollbackButtonKey))
          .onPressed,
      isNotNull,
    );
  });

  testWidgets(
    'compact 200 percent text keeps rollback recovery usable and announced',
    (tester) async {
      tester.view.physicalSize = const Size(320, 900);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final semantics = tester.ensureSemantics();
      final gateway = _PageGateway(releaseDetails())
        ..rollbackErrors.add(
          const ProtocolException('rollback coordinates did not match'),
        );

      await tester.pumpWidget(
        MaterialApp(
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(
              context,
            ).copyWith(textScaler: const TextScaler.linear(2)),
            child: child!,
          ),
          home: FeatureReleasePage(featureId: 'feature-a', gateway: gateway),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);

      final rollback = find.byKey(featureReleaseRollbackButtonKey);
      await tester.ensureVisible(rollback);
      await tester.tap(rollback);
      await tester.pumpAndSettle();
      expect(find.text('Roll back to the previous Version?'), findsOneWidget);
      expect(tester.takeException(), isNull);
      final confirm = find.byKey(featureReleaseConfirmRollbackButtonKey);
      await tester.ensureVisible(confirm);
      await tester.tap(confirm);
      await tester.pumpAndSettle();

      const recovery =
          'The rollback response could not be verified. Reload the Feature.';
      expect(
        find.byWidgetPredicate(
          (widget) =>
              widget is Semantics &&
              widget.properties.liveRegion == true &&
              widget.properties.label == recovery,
          skipOffstage: false,
        ),
        findsOneWidget,
      );
      final reload = find.byKey(featureReleaseReloadButtonKey);
      await tester.ensureVisible(reload);
      expect(tester.takeException(), isNull);

      gateway.loadResult = releaseDetails(revision: Int64(20));
      await tester.tap(reload);
      await tester.pumpAndSettle();

      expect(gateway.loadedFeatureIds, ['feature-a', 'feature-a']);
      expect(find.text('Active Version'), findsOneWidget);
      expect(tester.takeException(), isNull);
      semantics.dispose();
    },
  );
}

FeatureReleaseDetails _releaseDetailsWithoutAccess() {
  final details = releaseDetails(withPrevious: false);
  return FeatureReleaseDetails(
    featureId: details.featureId,
    installationId: details.installationId,
    revision: details.revision,
    originatingRequest: details.originatingRequest,
    activeVersion: FeatureReleaseVersion(
      digest: details.activeVersion.digest,
      sourceReference: details.activeVersion.sourceReference,
      sourceKind: details.activeVersion.sourceKind,
      requestedCapabilityIds: const [],
      dependencies: details.activeVersion.dependencies,
      source: details.activeVersion.source,
    ),
    previousVersion: details.previousVersion,
    activeGrants: const [],
    subscriptions: details.subscriptions,
    paused: details.paused,
    pauseReason: details.pauseReason,
  );
}

class _PageGateway implements FeatureReleaseGateway {
  _PageGateway(this.loadResult);

  FeatureReleaseDetails loadResult;
  FeatureReleaseDetails? rollbackResult;
  final List<Object> loadErrors = [];
  final List<String> loadedFeatureIds = [];
  final List<String?> expectedActiveDigests = [];
  final List<String> rollbackIds = [];
  final List<Object> rollbackErrors = [];

  @override
  Future<FeatureReleaseDetails> loadFeature(
    String featureId, {
    String? expectedActiveDigest,
  }) async {
    loadedFeatureIds.add(featureId);
    expectedActiveDigests.add(expectedActiveDigest);
    if (loadErrors.isNotEmpty) throw loadErrors.removeAt(0);
    return loadResult;
  }

  @override
  Future<FeatureReleaseDetails> rollbackFeature({
    required FeatureReleaseDetails current,
    required String idempotencyId,
  }) async {
    rollbackIds.add(idempotencyId);
    if (rollbackErrors.isNotEmpty) throw rollbackErrors.removeAt(0);
    return rollbackResult!;
  }
}
