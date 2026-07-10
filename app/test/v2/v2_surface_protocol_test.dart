import 'dart:convert';

import 'package:digitalbrain_flutter/v2/protocol/surface_protocol.dart';
import 'package:flutter_test/flutter_test.dart';

import 'v2_test_fixtures.dart';

void main() {
  test('advertises exactly the declared protocol capability versions', () {
    const capabilities = V2ClientCapabilities(protocolVersions: {3});

    expect(capabilities.names, contains('ui.protocol.v3'));
    expect(capabilities.names, isNot(contains('ui.protocol.v2')));
    expect(
      const V2ClientCapabilities().names,
      contains('ui.native.ino-conversation'),
    );
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

  test('decodes a typed INO conversation without opaque identifiers', () {
    final envelope = const SurfaceEnvelopeDecoder().decode(
      surfaceJsonString(
        payload: inoConversationPayload(
          messages: [
            inoMessage(role: 'user', text: 'Hello', state: 'queued'),
            inoMessage(
              role: 'assistant',
              text: 'How can I help?',
              state: 'succeeded',
            ),
          ],
          operation: inoOperation(state: 'succeeded'),
        ),
        actions: [testInoActionJson()],
      ),
    );

    final payload = envelope.payload as InoConversationSurfacePayload;
    expect(payload.intro, 'Ask INO about this workspace.');
    expect(payload.messages, hasLength(2));
    expect(payload.messages.first.turnKey, startsWith('turn-user-'));
    expect(payload.messages.first.role, InoConversationRole.user);
    expect(payload.messages.last.role, InoConversationRole.assistant);
    expect(payload.operation?.state, InoConversationOperationState.succeeded);
    expect(payload.operation?.retryable, isFalse);
    expect(envelope.actions.single.bindingId, 'ino.send');
    expect(envelope.actions.single.actionType, 'ino.interact');
  });

  test('rejects malformed or sensitive INO conversation data', () {
    final invalidPayloads = <Map<String, Object?>>[
      inoConversationPayload(
        messages: [inoMessage(role: 'system', text: 'Hidden', state: 'queued')],
      ),
      inoConversationPayload(operation: inoOperation(state: 'unknown')),
      inoConversationPayload(
        messages: [
          {
            ...inoMessage(role: 'user', text: 'Hello', state: 'queued'),
            'principalId': 'must-not-reach-renderer',
          },
        ],
      ),
      inoConversationPayload(
        messages: [
          inoMessage(
            role: 'user',
            text: 'Hello',
            state: 'queued',
            turnKey: 'not a safe key',
          ),
        ],
      ),
      inoConversationPayload(
        operation: {
          ...inoOperation(state: 'failed', retryable: true),
          'operationId': 'must-not-reach-renderer',
        },
      ),
      inoConversationPayload(
        operation: inoOperation(
          state: 'succeeded',
          action: googleConnectionAction(target: 'https://example.com/auth'),
        ),
      ),
    ];

    for (final payload in invalidPayloads) {
      expect(
        () => const SurfaceEnvelopeDecoder().decode(
          surfaceJsonString(payload: payload),
        ),
        throwsFormatException,
      );
    }
  });

  test('decodes only the bounded Google connection action', () {
    final envelope = const SurfaceEnvelopeDecoder().decode(
      surfaceJsonString(
        payload: inoConversationPayload(
          operation: inoOperation(
            state: 'succeeded',
            action: googleConnectionAction(),
          ),
        ),
      ),
    );

    final operation =
        (envelope.payload as InoConversationSurfacePayload).operation!;
    expect(operation.action?.kind, 'openUrl');
    expect(operation.action?.label, 'Connect Google');
    expect(operation.action?.target.host, 'accounts.google.com');
  });

  test('decodes the bounded Salesforce connection action', () {
    final envelope = const SurfaceEnvelopeDecoder().decode(
      surfaceJsonString(
        payload: inoConversationPayload(
          operation: inoOperation(
            state: 'succeeded',
            action: salesforceConnectionAction(),
          ),
        ),
      ),
    );

    final operation =
        (envelope.payload as InoConversationSurfacePayload).operation!;
    expect(operation.action?.kind, 'openUrl');
    expect(operation.action?.label, 'Connect Salesforce');
    expect(operation.action?.target.host, 'login.salesforce.com');
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
