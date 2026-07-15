import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_gateway.dart';
import 'package:digitalbrain_flutter/features/releases/feature_release_models.dart';
import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/runtime/runtime_errors.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';

import 'feature_release_test_fixtures.dart';

void main() {
  test('loads and validates the installed Feature release detail', () async {
    final client = _FeatureClient(wireReleaseDetails());
    final gateway = GrpcFeatureReleaseGateway(client: client);

    final details = await gateway.loadFeature('feature-a');

    expect(client.getRequests.single.featureId, 'feature-a');
    expect(
      client.sourceRequests
          .map(
            (request) => (
              request.featureId,
              request.installationId,
              request.releaseDigest,
              request.sourceReference,
            ),
          )
          .toList(),
      [
        (
          'feature-a',
          'installation-a',
          releaseDigest('a'),
          sourceReference('a'),
        ),
        (
          'feature-a',
          'installation-a',
          releaseDigest('b'),
          sourceReference('b'),
        ),
      ],
    );
    expect(details.featureId, 'feature-a');
    expect(details.installationId, 'installation-a');
    expect(details.revision, Int64(12));
    expect(details.originatingRequest.text, 'Research Acme');
    expect(details.activeVersion.digest, releaseDigest('a'));
    expect(
      details.activeVersion.sourceKind,
      FeatureReleaseSourceKind.runtimeAuthored,
    );
    expect(details.previousVersion?.digest, releaseDigest('b'));
    expect(details.activeGrants, hasLength(2));
    expect(details.activeGrants.first.provider, 'Google');
    expect(details.activeGrants.first.connectionId, 'connection-acme');
    expect(
      details.activeGrants.first.constraintSummary,
      'Only digitalbrain.integration.email.read; input limit must equal 25; '
      'input mailbox must equal "inbox"',
    );
    expect(details.subscriptions, ['manual', 'schedule:weekday']);
    expect(details.rollbackAvailable, isTrue);
  });

  test('loads the exact active Version requested by a deep link', () async {
    final client = _FeatureClient(wireReleaseDetails());
    final gateway = GrpcFeatureReleaseGateway(client: client);

    final details = await gateway.loadFeature(
      'feature-a',
      expectedActiveDigest: releaseDigest('a'),
    );

    expect(details.activeVersion.digest, releaseDigest('a'));
    expect(client.getRequests, hasLength(1));
    expect(client.sourceRequests, hasLength(2));
  });

  test(
    'rejects a deep link to a different active Version before hydration',
    () async {
      final client = _FeatureClient(wireReleaseDetails());
      final gateway = GrpcFeatureReleaseGateway(client: client);

      await expectLater(
        gateway.loadFeature(
          'feature-a',
          expectedActiveDigest: releaseDigest('b'),
        ),
        throwsA(isA<ProtocolException>()),
      );
      expect(client.getRequests, hasLength(1));
      expect(client.sourceRequests, isEmpty);
    },
  );

  test(
    'rejects a malformed deep-link Version before sending a request',
    () async {
      final client = _FeatureClient(wireReleaseDetails());
      final gateway = GrpcFeatureReleaseGateway(client: client);

      await expectLater(
        gateway.loadFeature('feature-a', expectedActiveDigest: 'not-a-digest'),
        throwsArgumentError,
      );
      expect(client.getRequests, isEmpty);
      expect(client.sourceRequests, isEmpty);
    },
  );

  test('rejects a response with more than 32 requested capabilities', () async {
    final fixture = wireReleaseDetails(withPrevious: false);
    final capabilityIds = List.generate(
      33,
      (index) => 'digitalbrain.test.capability.$index',
    );
    fixture.activeRelease.requestedCapabilityIds
      ..clear()
      ..addAll(capabilityIds);
    fixture.activeGrants
      ..clear()
      ..addAll(
        capabilityIds.map(
          (capabilityId) => wire.FeatureGrant(
            capabilityId: capabilityId,
            capabilityVersion: 1,
            constraintsJson: '{"allowedToolIds":["$capabilityId"]}',
          ),
        ),
      );
    final client = _FeatureClient(fixture);
    final gateway = GrpcFeatureReleaseGateway(client: client);

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
    expect(client.sourceRequests, isEmpty);
  });

  test('release Version model rejects more than 32 requested capabilities', () {
    final valid = releaseVersion('a');

    expect(
      () => FeatureReleaseVersion(
        digest: valid.digest,
        sourceReference: valid.sourceReference,
        sourceKind: valid.sourceKind,
        requestedCapabilityIds: List.generate(
          33,
          (index) => 'digitalbrain.test.capability.$index',
        ),
        dependencies: valid.dependencies,
        source: valid.source,
      ),
      throwsArgumentError,
    );
  });

  test(
    'rejects a response identity containing a C1 control character',
    () async {
      final fixture = wireReleaseDetails(withPrevious: false)
        ..installationId = 'installation\u0085a';
      final gateway = GrpcFeatureReleaseGateway(
        client: _FeatureClient(fixture),
      );

      await expectLater(
        gateway.loadFeature('feature-a'),
        throwsA(isA<ProtocolException>()),
      );
    },
  );

  test('accepts 4096-character non-ASCII origin and pause text', () async {
    final boundaryText = List.filled(4096, 'é').join();
    final fixture = wireReleaseDetails(withPrevious: false, paused: true)
      ..originatingRequest.text = boundaryText
      ..pauseReason = boundaryText;
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    final details = await gateway.loadFeature('feature-a');

    expect(details.originatingRequest.text.length, 4096);
    expect(details.pauseReason?.length, 4096);
  });

  test('rejects noncanonical origin text after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(withPrevious: false)
      ..originatingRequest.text = 'Research Acme\n';
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects C1 control in pause text after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(withPrevious: false, paused: true)
      ..pauseReason = 'Connection\u0085revoked';
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects origin and pause text beyond 4096 characters', () async {
    final oversizedText = List.filled(4097, 'a').join();
    final originFixture = wireReleaseDetails(withPrevious: false)
      ..originatingRequest.text = oversizedText;
    final pauseFixture = wireReleaseDetails(withPrevious: false, paused: true)
      ..pauseReason = oversizedText;

    for (final fixture in [originFixture, pauseFixture]) {
      final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
      final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));
      await expectLater(
        gateway.loadFeature('feature-a'),
        throwsA(isA<ProtocolException>()),
      );
    }
  });

  test('rejects a paused response that retains a rollback boundary', () async {
    final client = _FeatureClient(wireReleaseDetails(paused: true));
    final gateway = GrpcFeatureReleaseGateway(client: client);

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
    expect(client.sourceRequests, isEmpty);
  });

  test('release authority rejects paused state with a previous Version', () {
    expect(() => releaseDetails(paused: true), throwsArgumentError);
  });

  test('rejects a provider-only grant after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(withPrevious: false);
    fixture.activeGrants.first.clearConnectionId();
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final grant = reply.activeGrants.first;
    expect(grant.hasProvider(), isTrue);
    expect(grant.hasConnectionId(), isFalse);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects a connection-only grant after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(withPrevious: false);
    fixture.activeGrants.first.clearProvider();
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final grant = reply.activeGrants.first;
    expect(grant.hasProvider(), isFalse);
    expect(grant.hasConnectionId(), isTrue);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  for (final malformed in <({String name, String constraintsJson})>[
    (name: 'missing allowlist', constraintsJson: '{}'),
    (
      name: 'unknown root property',
      constraintsJson:
          '{"allowedToolIds":["digitalbrain.integration.email.read"],"unknown":true}',
    ),
    (
      name: 'mismatched allowlist',
      constraintsJson: '{"allowedToolIds":["digitalbrain.model.generate"]}',
    ),
    (
      name: 'malformed allowlist',
      constraintsJson:
          '{"allowedToolIds":"digitalbrain.integration.email.read"}',
    ),
    (name: 'empty allowlist', constraintsJson: '{"allowedToolIds":[]}'),
    (
      name: 'duplicate allowlist',
      constraintsJson:
          '{"allowedToolIds":["digitalbrain.integration.email.read","digitalbrain.integration.email.read"]}',
    ),
    (
      name: 'malformed payload',
      constraintsJson:
          '{"allowedToolIds":["digitalbrain.integration.email.read"],"payload":[]}',
    ),
  ]) {
    test('rejects ${malformed.name} grant constraints', () async {
      final fixture = wireReleaseDetails(withPrevious: false);
      fixture.activeGrants.first.constraintsJson = malformed.constraintsJson;
      final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
      final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

      await expectLater(
        gateway.loadFeature('feature-a'),
        throwsA(isA<ProtocolException>()),
      );
    });
  }

  test('rejects an installed authority without subscriptions', () async {
    final fixture = wireReleaseDetails(withPrevious: false)
      ..subscriptions.clear();
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects an unpaused response with a pause reason', () async {
    final fixture = wireReleaseDetails(withPrevious: false)
      ..pauseReason = 'Unexpected pause state';
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    expect(reply.paused, isFalse);
    expect(reply.hasPauseReason(), isTrue);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects a paused response without a pause reason', () async {
    final fixture = wireReleaseDetails(withPrevious: false, paused: true)
      ..clearPauseReason();
    final reply = wire.FeatureReply.fromBuffer(fixture.writeToBuffer());
    expect(reply.paused, isTrue);
    expect(reply.hasPauseReason(), isFalse);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  for (final binding
      in <({String name, String? provider, String? connectionId})>[
        (name: 'provider-only', provider: 'Google', connectionId: null),
        (
          name: 'connection-only',
          provider: null,
          connectionId: 'connection-acme',
        ),
      ]) {
    test('release authority rejects ${binding.name} grant binding', () {
      final valid = releaseDetails();
      final original = valid.activeGrants.first;
      final asymmetric = FeatureReleaseGrant(
        capabilityId: original.capabilityId,
        capabilityVersion: original.capabilityVersion,
        provider: binding.provider,
        connectionId: binding.connectionId,
        constraintsJson: original.constraintsJson,
        constraintSummary: original.constraintSummary,
      );

      expect(
        () => FeatureReleaseDetails(
          featureId: valid.featureId,
          installationId: valid.installationId,
          revision: valid.revision,
          originatingRequest: valid.originatingRequest,
          activeVersion: valid.activeVersion,
          previousVersion: valid.previousVersion,
          activeGrants: [asymmetric, valid.activeGrants.last],
          subscriptions: valid.subscriptions,
          paused: valid.paused,
          pauseReason: valid.pauseReason,
        ),
        throwsArgumentError,
      );
    });
  }

  test('release authority rejects a forged constraint summary', () {
    final valid = releaseDetails();
    final original = valid.activeGrants.first;
    final forged = FeatureReleaseGrant(
      capabilityId: original.capabilityId,
      capabilityVersion: original.capabilityVersion,
      provider: original.provider,
      connectionId: original.connectionId,
      constraintsJson: original.constraintsJson,
      constraintSummary: 'Unrestricted access',
    );

    expect(
      () =>
          _copyDetails(valid, activeGrants: [forged, valid.activeGrants.last]),
      throwsArgumentError,
    );
  });

  test('release authority rejects empty subscriptions', () {
    final valid = releaseDetails();

    expect(
      () => _copyDetails(valid, subscriptions: const []),
      throwsArgumentError,
    );
  });

  test('accepts false authority flags after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(withPrevious: false)
      ..clearRollbackAvailable()
      ..clearPaused();
    final encoded = fixture.writeToBuffer();
    final reply = wire.FeatureReply.fromBuffer(encoded);
    expect(reply.hasRollbackAvailable(), isFalse);
    expect(reply.hasPaused(), isFalse);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    final details = await gateway.loadFeature('feature-a');

    expect(details.rollbackAvailable, isFalse);
    expect(details.paused, isFalse);
    expect(details.revision, Int64(12));
  });

  test('rejects revision zero after protobuf round-trip', () async {
    final fixture = wireReleaseDetails(
      withPrevious: false,
      revision: Int64.ZERO,
    )..clearRevision();
    final encoded = fixture.writeToBuffer();
    final reply = wire.FeatureReply.fromBuffer(encoded);
    expect(reply.hasRevision(), isFalse);
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects an inconsistent rollback boundary', () async {
    final reply = wireReleaseDetails(withPrevious: false)
      ..rollbackAvailable = true;
    final gateway = GrpcFeatureReleaseGateway(client: _FeatureClient(reply));

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects a dedicated release-source response without source', () async {
    final sourceReply = wireReleaseSource('a')..clearSource();
    final client = _FeatureClient(wireReleaseDetails())
      ..sourceReplyOverride = sourceReply;
    final gateway = GrpcFeatureReleaseGateway(client: client);

    await expectLater(
      gateway.loadFeature('feature-a'),
      throwsA(isA<ProtocolException>()),
    );
  });

  test(
    'hydrates a valid empty source file after protobuf round-trip',
    () async {
      final sourceReply = wireReleaseSource('a');
      sourceReply.source.files.add(
        wire.FeatureSourceFile(path: 'Feature/Empty.cs'),
      );
      final overTheWire = wire.FeatureReleaseSourceReply.fromBuffer(
        sourceReply.writeToBuffer(),
      );
      final client = _FeatureClient(wireReleaseDetails(withPrevious: false))
        ..sourceReplyOverride = overTheWire;
      final gateway = GrpcFeatureReleaseGateway(client: client);

      final details = await gateway.loadFeature('feature-a');

      expect(
        details.activeVersion.source.files
            .singleWhere((file) => file.path == 'Feature/Empty.cs')
            .content,
        isEmpty,
      );
    },
  );

  for (final mismatch in <String, wire.FeatureReleaseSourceReply Function()>{
    'feature identity': () => wireReleaseSource('a', featureId: 'feature-b'),
    'installation identity': () =>
        wireReleaseSource('a', installationId: 'installation-b'),
    'release digest': () =>
        wireReleaseSource('a', releaseDigestOverride: releaseDigest('c')),
    'source reference': () =>
        wireReleaseSource('a', sourceReferenceOverride: sourceReference('c')),
  }.entries) {
    test('rejects release source with mismatched ${mismatch.key}', () async {
      final client = _FeatureClient(wireReleaseDetails())
        ..sourceReplyOverride = mismatch.value();
      final gateway = GrpcFeatureReleaseGateway(client: client);

      await expectLater(
        gateway.loadFeature('feature-a'),
        throwsA(isA<ProtocolException>()),
      );
    });
  }

  test('sends a fenced idempotent rollback to the previous Version', () async {
    final client = _FeatureClient(wireReleaseDetails());
    final gateway = GrpcFeatureReleaseGateway(client: client);
    final current = await gateway.loadFeature('feature-a');
    client.reply = wireReleaseDetails(
      activeCharacter: 'b',
      withPrevious: false,
      revision: Int64(13),
    );

    final restored = await gateway.rollbackFeature(
      current: current,
      idempotencyId: 'rollback-request-a',
    );

    final request = client.rollbackRequests.single;
    expect(request.featureId, 'feature-a');
    expect(request.expectedActiveDigest, releaseDigest('a'));
    expect(request.targetDigest, releaseDigest('b'));
    expect(request.idempotencyId, 'rollback-request-a');
    expect(request.expectedRevision, Int64(12));
    expect(restored.activeVersion.digest, releaseDigest('b'));
    expect(restored.revision, Int64(13));
    expect(restored.previousVersion, isNull);
  });

  test('rejects a rollback response whose revision did not advance', () async {
    final client = _FeatureClient(wireReleaseDetails());
    final gateway = GrpcFeatureReleaseGateway(client: client);
    final current = await gateway.loadFeature('feature-a');
    client.reply = wireReleaseDetails(
      activeCharacter: 'b',
      withPrevious: false,
      revision: Int64(12),
    );

    await expectLater(
      gateway.rollbackFeature(
        current: current,
        idempotencyId: 'rollback-request-a',
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('rejects a rollback response that skips a revision', () async {
    final client = _FeatureClient(wireReleaseDetails());
    final gateway = GrpcFeatureReleaseGateway(client: client);
    final current = await gateway.loadFeature('feature-a');
    client.reply = wireReleaseDetails(
      activeCharacter: 'b',
      withPrevious: false,
      revision: Int64(14),
    );

    await expectLater(
      gateway.rollbackFeature(
        current: current,
        idempotencyId: 'rollback-request-a',
      ),
      throwsA(isA<ProtocolException>()),
    );
  });

  test('does not send rollback when the revision cannot advance', () async {
    final client = _FeatureClient(
      wireReleaseDetails(revision: Int64.MAX_VALUE),
    );
    final gateway = GrpcFeatureReleaseGateway(client: client);
    final current = await gateway.loadFeature('feature-a');

    await expectLater(
      gateway.rollbackFeature(
        current: current,
        idempotencyId: 'rollback-request-a',
      ),
      throwsA(isA<PreconditionException>()),
    );
    expect(client.rollbackRequests, isEmpty);
  });
}

FeatureReleaseDetails _copyDetails(
  FeatureReleaseDetails value, {
  List<FeatureReleaseGrant>? activeGrants,
  List<String>? subscriptions,
}) => FeatureReleaseDetails(
  featureId: value.featureId,
  installationId: value.installationId,
  revision: value.revision,
  originatingRequest: value.originatingRequest,
  activeVersion: value.activeVersion,
  previousVersion: value.previousVersion,
  activeGrants: activeGrants ?? value.activeGrants,
  subscriptions: subscriptions ?? value.subscriptions,
  paused: value.paused,
  pauseReason: value.pauseReason,
);

class _FeatureClient implements FeatureAuthoringClient {
  _FeatureClient(this.reply);

  wire.FeatureReply reply;
  final List<wire.GetFeatureRequest> getRequests = [];
  final List<wire.GetFeatureReleaseSourceRequest> sourceRequests = [];
  final List<wire.RollbackFeatureVersionRequest> rollbackRequests = [];
  wire.FeatureReleaseSourceReply? sourceReplyOverride;

  @override
  Future<wire.FeatureReply> getFeature(wire.GetFeatureRequest request) async {
    getRequests.add(request.deepCopy());
    return reply.deepCopy();
  }

  @override
  Future<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
  ) async {
    rollbackRequests.add(request.deepCopy());
    return reply.deepCopy();
  }

  @override
  Future<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
  ) async {
    sourceRequests.add(request.deepCopy());
    final override = sourceReplyOverride;
    if (override != null) return override.deepCopy();
    return wireReleaseSource(request.releaseDigest[0]).deepCopy();
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}
