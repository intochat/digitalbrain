import 'dart:convert';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_test_fixtures.dart';

void main() {
  test('advertises exactly the declared protocol capability versions', () {
    const capabilities = V2ClientCapabilities(protocolVersions: {3});

    expect(capabilities.names, contains('ui.protocol.v3'));
    expect(capabilities.names, isNot(contains('ui.protocol.v2')));
  });

  test('decodes a complete V2 SurfaceEnvelope and typed action binding', () {
    final envelope = const SurfaceEnvelopeDecoder().decode(
      surfaceJsonString(actions: [testActionJson()]),
    );

    expect(envelope.protocolVersion, 2);
    expect(envelope.tenantId, 'tenant-a');
    expect(envelope.workspaceId, 'workspace-a');
    expect(envelope.payload, isA<NativeSurfacePayload>());
    expect(envelope.actions.single.bindingId, 'refresh-binding');
    expect(envelope.actions.single.actionToken, 'signed-action-token');
  });

  test('rejects unsupported protocol and capability requirements', () {
    final wrongVersion = surfaceJsonMap()..['protocolVersion'] = 3;
    final unsupported = surfaceJsonMap()
      ..['requiredClientCapabilities'] = ['ui.payload.future'];

    expect(
      () => const SurfaceEnvelopeDecoder().decode(jsonEncode(wrongVersion)),
      throwsFormatException,
    );
    expect(
      () => const SurfaceEnvelopeDecoder().decode(jsonEncode(unsupported)),
      throwsA(isA<UnsupportedSurfaceCapability>()),
    );
  });

  test(
    'rejects credentials and private identifiers hidden in payload data',
    () {
      for (final key in [
        'accessToken',
        'refresh_token',
        'action-token',
        'tenantId',
        'workspace_id',
        'clientId',
        'grants',
        'principal',
        'principalId',
        'secret',
        'session-id',
      ]) {
        final source = surfaceJsonString(
          payload: {
            'kind': 'native',
            'nativeKind': 'message',
            'data': {key: 'must-not-reach-renderer'},
          },
        );
        expect(
          () => const SurfaceEnvelopeDecoder().decode(source),
          throwsFormatException,
          reason: key,
        );
      }
    },
  );

  test('rejects action binding for the wrong surface revision', () {
    final source = surfaceJsonString(
      actions: [testActionJson(surfaceRevision: 2)],
    );

    expect(
      () => const SurfaceEnvelopeDecoder().decode(source),
      throwsFormatException,
    );
  });

  test('enforces the negotiated envelope byte limit', () {
    final decoder = SurfaceEnvelopeDecoder(
      capabilities: const V2ClientCapabilities(maximumPayloadBytes: 32),
    );

    expect(() => decoder.decode(surfaceJsonString()), throwsFormatException);
  });
}
