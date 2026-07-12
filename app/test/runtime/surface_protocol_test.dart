import 'dart:convert';

import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:flutter_test/flutter_test.dart';

import 'test_fixtures.dart';

void main() {
  test('advertises exactly the declared protocol capability versions', () {
    const capabilities = ClientCapabilities(protocolVersions: {3});

    expect(capabilities.names, contains('ui.protocol.v3'));
    expect(capabilities.names, isNot(contains('ui.protocol.v2')));
    expect(
      const ClientCapabilities().names,
      contains('ui.native.ino-conversation'),
    );
  });

  test('decodes a complete SurfaceEnvelope and typed action binding', () {
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
    final decoder = SurfaceEnvelopeDecoder(
      oauthStartOrigin: Uri.parse('https://brain.example:7443'),
    );
    final tooLongFlow = List<String>.filled(1025, 'a').join();
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
      for (final target in [
        'https://accounts.google.com/',
        'https://accounts.google.com:444/o/oauth2/v2/auth?state=opaque',
        'https://user@accounts.google.com/o/oauth2/v2/auth?state=opaque',
        'https://accounts.google.com/o/oauth2/v2/auth?state=opaque#fragment',
        'https://brain.example:7443/oauth/start/google?f=0123456789abcdefghijklmnopqrstuv',
        '/oauth/start/google?t=0123456789abcdefghijklmnopqrstuv',
        '/oauth/start/google?f=too-short',
        '/oauth/start/google?f=0123456789abcdefghijklmnopqrstu%',
        '/oauth/start/google?f=$tooLongFlow',
        '/oauth/start/google?f=0123456789abcdefghijklmnopqrstuv&state=provider-state',
        '/oauth/start/google?f=0123456789abcdefghijklmnopqrstuv#fragment',
      ])
        inoConversationPayload(
          operation: inoOperation(
            state: 'succeeded',
            action: googleConnectionAction(target: target),
          ),
        ),
      for (final target in [
        'http://brain.example/oauth/start/salesforce?t=opaque-token',
        'https://evil.example/oauth/start/salesforce?t=opaque-token',
        'https://login.salesforce.com/services/oauth2/authorize?response_type=code',
        'http://localhost:51014/oauth/callback/salesforce?t=opaque-token',
        'http://localhost:51014/oauth/start/salesforce?t=opaque-token&state=provider-state',
        'http://localhost:51014/oauth/start/salesforce?t=',
        'http://user@localhost:51014/oauth/start/salesforce?t=opaque-token',
        'http://localhost:51014/oauth/start/salesforce?t=opaque-token#fragment',
        '/oauth/start/salesforce?t=0123456789abcdefghijklmnopqrstuv',
        '/oauth/start/salesforce?f=too-short',
        '/oauth/start/unknown?f=0123456789abcdefghijklmnopqrstuv',
      ])
        inoConversationPayload(
          operation: inoOperation(
            state: 'succeeded',
            action: salesforceConnectionAction(target: target),
          ),
        ),
    ];

    for (final payload in invalidPayloads) {
      expect(
        () => decoder.decode(surfaceJsonString(payload: payload)),
        throwsFormatException,
      );
    }
  });

  test('decodes only the bounded Google connection action', () {
    final envelope =
        SurfaceEnvelopeDecoder(
          oauthStartOrigin: Uri.parse('https://brain.example:7443'),
        ).decode(
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
    expect(operation.action?.target.host, 'brain.example');
    expect(operation.action?.target.path, '/oauth/start/google');
    expect(operation.action?.target.queryParameters.keys, ['f']);
  });

  test('decodes the bounded Salesforce connection action', () {
    final envelope =
        SurfaceEnvelopeDecoder(
          oauthStartOrigin: Uri.parse('https://brain.example:7443'),
        ).decode(
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
    expect(operation.action?.target.host, 'brain.example');
    expect(operation.action?.target.path, '/oauth/start/salesforce');
    expect(operation.action?.target.queryParameters.keys, ['f']);
  });

  test('requires a trusted HTTPS runtime origin for connection actions', () {
    final decoder = SurfaceEnvelopeDecoder(
      oauthStartOrigin: Uri.parse('https://brain.example:7443'),
    );
    final accepted = decoder.decode(
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
        (accepted.payload as InoConversationSurfacePayload).operation!;
    expect(operation.action?.target.host, 'brain.example');
    for (final origin in [
      'http://localhost:7443',
      'https://user@brain.example:7443',
      'https://brain.example:7443/runtime',
    ]) {
      expect(
        () =>
            SurfaceEnvelopeDecoder(oauthStartOrigin: Uri.parse(origin)).decode(
              surfaceJsonString(
                payload: inoConversationPayload(
                  operation: inoOperation(
                    state: 'succeeded',
                    action: salesforceConnectionAction(),
                  ),
                ),
              ),
            ),
        throwsFormatException,
      );
    }
    expect(
      () => const SurfaceEnvelopeDecoder().decode(
        surfaceJsonString(
          payload: inoConversationPayload(
            operation: inoOperation(
              state: 'succeeded',
              action: googleConnectionAction(),
            ),
          ),
        ),
      ),
      throwsFormatException,
    );
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
      capabilities: const ClientCapabilities(maximumPayloadBytes: 32),
    );

    expect(() => decoder.decode(surfaceJsonString()), throwsFormatException);
  });
}
