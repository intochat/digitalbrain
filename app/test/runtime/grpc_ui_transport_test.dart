import 'dart:async';

import 'package:digitalbrain_flutter/grpc/ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/runtime/runtime_configuration.dart';
import 'package:digitalbrain_flutter/runtime/grpc_ui_transport.dart';
import 'package:digitalbrain_flutter/runtime/protocol/surface_protocol.dart';
import 'package:digitalbrain_flutter/runtime/runtime.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:grpc/grpc.dart';

import 'test_fixtures.dart';

void main() {
  group('GrpcUiTransport', () {
    test(
      'production channel disables metadata-bearing timeline logging',
      () async {
        isTimelineLoggingEnabled = true;
        final transport = GrpcUiTransport.connect(
          Uri.parse('https://localhost:7443'),
        );

        expect(isTimelineLoggingEnabled, isFalse);
        await transport.close();
      },
    );

    test('production channel refuses a plaintext endpoint', () {
      expect(
        () => GrpcUiTransport.connect(Uri.parse('http://localhost:5080')),
        throwsArgumentError,
      );
    });

    test(
      'bootstrap exchanges the credential for the exact UI audience',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        final session = await transport.bootstrapSession('bootstrap-once');

        expect(port.bootstrapRequest?.secret, 'bootstrap-once');
        expect(port.bootstrapOptions?.metadata, {
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(
          port.bootstrapOptions?.metadata,
          isNot(contains('x-v2-session')),
        );
        expect(port.bootstrapOptions?.timeout, unaryRequestTimeout);
        expect(session.identity.ownerId, 'owner-a');
        expect(session.credentials.accessToken, 'access-token');
      },
    );

    test(
      'external bootstrap sends bearer identity and an empty bootstrap secret',
      () async {
        const identityToken =
            'identityheader.identitypayload.identitysignature';
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        final session = await transport.bootstrapExternalSession(identityToken);

        expect(port.bootstrapRequest?.secret, isEmpty);
        expect(port.bootstrapOptions?.metadata, {
          'authorization': 'Bearer $identityToken',
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(
          port.bootstrapOptions?.metadata,
          isNot(contains('x-v2-session')),
        );
        expect(port.bootstrapOptions?.timeout, unaryRequestTimeout);
        expect(session.identity.sessionId, 'session-a');
      },
    );

    test(
      'external bootstrap rejects malformed compact identity tokens',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        for (final token in [
          '',
          'identity-token-without-compact-segments',
          'header.payload',
          ' headerheader.payloadpayload.signaturesignature',
          'headerheader.payload payload.signaturesignature',
        ]) {
          await expectLater(
            transport.bootstrapExternalSession(token),
            throwsA(isA<AuthenticationException>()),
          );
        }

        expect(port.bootstrapRequest, isNull);
        expect(port.bootstrapOptions, isNull);
      },
    );

    test(
      'refresh sends exact audience and never requires expired access',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        await transport.refreshSession(refreshToken: 'refresh-opaque');

        expect(port.refreshRequest?.refreshToken, 'refresh-opaque');
        expect(port.refreshOptions?.metadata, {
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(port.refreshOptions?.metadata, isNot(contains('x-v2-session')));
        expect(port.refreshOptions?.timeout, unaryRequestTimeout);
      },
    );

    test(
      'logout revokes with the opaque refresh token and UI audience',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        await transport.logout(refreshToken: 'refresh-opaque');

        expect(port.logoutRequest?.refreshToken, 'refresh-opaque');
        expect(port.logoutOptions?.metadata, {
          'x-v2-audience': digitalBrainUiAudience,
        });
      },
    );

    test(
      'feed sends signed metadata, resume, capabilities, and maps reset',
      () async {
        final response = _FakeGrpcFeedResponse(
          Stream.fromIterable([
            wire.SurfaceFeedEvent(surfaceJson: surfaceJsonString(sequence: 8)),
            wire.SurfaceFeedEvent(
              reset: wire.SurfaceFeedReset(
                reason: 'retention-gap',
                resumeSequence: Int64(12),
                snapshotJson: [surfaceJsonString(sequence: 11)],
              ),
            ),
          ]),
        );
        final port = _FakeGrpcClientPort()..feedResponse = response;
        final transport = GrpcUiTransport.forTesting(client: port);

        final call = await transport.watchSurfaceFeed(
          accessToken: 'signed-session',
          afterSequence: 7,
          audience: FeedAudience.actor,
          clientCapabilities: const {'ui.payload.native', 'ui.protocol.v2'},
          maxBatchSize: 25,
        );
        final events = await call.events.toList();

        expect(port.watchRequest?.afterSequence.toInt(), 7);
        expect(
          port.watchRequest?.audience,
          wire.FeedAudienceKind.FEED_AUDIENCE_KIND_ACTOR,
        );
        expect(port.watchRequest?.clientCapabilities, [
          'ui.payload.native',
          'ui.protocol.v2',
        ]);
        expect(port.watchRequest?.maxBatchSize, 25);
        expect(port.watchOptions?.metadata, {
          'x-v2-session': 'signed-session',
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(port.watchOptions?.timeout, isNull);
        expect(events.first, isA<FeedSurfaceJson>());
        final reset = events.last as FeedResetEvent;
        expect(reset.reason, 'retention-gap');
        expect(reset.resumeSequence, 12);
        expect(reset.snapshotJson, hasLength(1));

        await call.cancel();
        expect(response.cancelled, isTrue);
      },
    );

    test('acknowledgement and action use signed session metadata', () async {
      final port = _FakeGrpcClientPort();
      final transport = GrpcUiTransport.forTesting(client: port);
      final surface = testSurface(actions: [testActionJson()]);

      await transport.acknowledgeSurfaceFeed(
        accessToken: 'signed-session',
        audience: FeedAudience.actor,
        sequence: 4,
      );
      final result = await transport.submitAction(
        accessToken: 'signed-session',
        action: surface.actions.single,
        input: const {'confirmed': true},
      );

      expect(port.ackOptions?.metadata, {
        'x-v2-session': 'signed-session',
        'x-v2-audience': digitalBrainUiAudience,
      });
      expect(port.actionOptions?.metadata, port.ackOptions?.metadata);
      expect(port.ackOptions?.timeout, unaryRequestTimeout);
      expect(port.actionOptions?.timeout, unaryRequestTimeout);
      expect(port.actionRequest?.bindingId, 'refresh-binding');
      expect(port.actionRequest?.actionToken, 'signed-action-token');
      expect(port.actionRequest?.surfaceRevision, 1);
      expect(port.actionRequest?.inputJson, '{"confirmed":true}');
      expect(result.operationId, 'operation-a');
    });

    test('maps a stale action precondition to a safe rejection', () async {
      final port = _FakeGrpcClientPort()
        ..actionError = GrpcError.failedPrecondition(
          'surface revision 1 is stale and must not escape',
        );
      final transport = GrpcUiTransport.forTesting(client: port);
      final action = testSurface(actions: [testActionJson()]).actions.single;

      await expectLater(
        transport.submitAction(
          accessToken: 'signed-session',
          action: action,
          input: const {'confirmed': true},
        ),
        throwsA(
          isA<PreconditionException>().having(
            (error) => error.safeMessage,
            'safeMessage',
            'UI action is stale. Refresh and try again.',
          ),
        ),
      );
    });

    test('close cancels an in-flight unary response', () async {
      final pending = Completer<wire.AcknowledgeSurfaceFeedReply>();
      final response = _FakeGrpcUnaryResponse(
        pending.future,
        onCancel: () async {
          if (!pending.isCompleted) {
            pending.completeError(GrpcError.cancelled('cancelled'));
          }
        },
      );
      final port = _FakeGrpcClientPort()..ackResponse = response;
      final transport = GrpcUiTransport.forTesting(client: port);

      final acknowledgement = transport.acknowledgeSurfaceFeed(
        accessToken: 'signed-session',
        audience: FeedAudience.actor,
        sequence: 4,
      );
      final expectation = expectLater(
        acknowledgement,
        throwsA(
          isA<TransportException>().having(
            (error) => error.code,
            'code',
            TransportErrorCode.cancelled,
          ),
        ),
      );
      await Future<void>.delayed(Duration.zero);

      await transport.close();
      await expectation;

      expect(response.cancelled, isTrue);
    });

    test(
      'rejects anonymous calls and private fields in action input',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);
        final action = testSurface(actions: [testActionJson()]).actions.single;

        await expectLater(
          transport.watchSurfaceFeed(
            accessToken: '',
            afterSequence: 0,
            audience: FeedAudience.actor,
            clientCapabilities: const {},
            maxBatchSize: 1,
          ),
          throwsA(isA<AuthenticationException>()),
        );
        await expectLater(
          transport.submitAction(
            accessToken: 'signed-session',
            action: action,
            input: const {'ownerId': 'must-not-travel'},
          ),
          throwsA(isA<ProtocolException>()),
        );
        expect(port.actionRequest, isNull);
      },
    );

    test('delivers a missing capability receipt and feature proposal through '
        'the runtime controller', () async {
      final response = _FakeGrpcFeedResponse(
        Stream.fromIterable([
          wire.SurfaceFeedEvent(
            surfaceJson: surfaceJsonString(
              payload: inoConversationPayload(
                operation: inoOperation(
                  state: 'succeeded',
                  capability: inoCapability(
                    kind: 'missing',
                    id: 'assistant.answer',
                    name: 'Assistant answer',
                    confidence: 0,
                  ),
                  proposal: inoFeatureProposal(),
                ),
              ),
            ),
          ),
        ]),
      );
      final port = _FakeGrpcClientPort()..feedResponse = response;
      final transport = GrpcUiTransport.forTesting(client: port);
      final runtime = RuntimeController(transport: transport);

      await runtime.authenticateWithBootstrap('bootstrap-once');
      await _eventually(() => runtime.latestSurface != null);

      final payload =
          runtime.latestSurface!.payload as InoConversationSurfacePayload;
      final operation = payload.operation!;
      expect(operation.capability?.kind, InoCapabilityResolutionKind.missing);
      expect(operation.proposal, isNotNull);
      expect(
        operation.proposal!.id,
        'proposal-0123456789abcdef0123456789abcdef',
      );
      expect(
        InoFeatureProposalReference.routeShape.hasMatch(
          operation.proposal!.route,
        ),
        isTrue,
      );
      expect(
        operation.proposal!.route,
        '/features/proposals/${operation.proposal!.id}',
      );

      await runtime.stop();
    });

    test(
      'maps authentication errors without retaining server details',
      () async {
        final port = _FakeGrpcClientPort()
          ..bootstrapError = GrpcError.unauthenticated(
            'access_token=must-not-escape',
          );
        final transport = GrpcUiTransport.forTesting(client: port);

        Object? caught;
        try {
          await transport.bootstrapSession('bad');
        } catch (error) {
          caught = error;
        }

        expect(caught, isA<AuthenticationException>());
        expect(caught.toString(), isNot(contains('must-not-escape')));
        expect(caught.toString(), isNot(contains('access_token')));
      },
    );
  });
}

Future<void> _eventually(bool Function() condition) async {
  for (var attempt = 0; attempt < 100; attempt++) {
    if (condition()) return;
    await Future<void>.delayed(const Duration(milliseconds: 1));
  }
  fail('Condition was not reached.');
}

class _FakeGrpcClientPort implements GrpcClientPort {
  wire.BootstrapSessionRequest? bootstrapRequest;
  CallOptions? bootstrapOptions;
  wire.RefreshSessionRequest? refreshRequest;
  CallOptions? refreshOptions;
  wire.LogoutSessionRequest? logoutRequest;
  CallOptions? logoutOptions;
  wire.WatchSurfaceFeedRequest? watchRequest;
  CallOptions? watchOptions;
  wire.AcknowledgeSurfaceFeedRequest? ackRequest;
  CallOptions? ackOptions;
  wire.SubmitActionRequest? actionRequest;
  CallOptions? actionOptions;
  Object? bootstrapError;
  Object? actionError;
  GrpcFeedResponse? feedResponse;
  GrpcUnaryResponse<wire.AcknowledgeSurfaceFeedReply>? ackResponse;

  wire.SessionReply get sessionReply => wire.SessionReply(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessExpiresAtUnixMs: Int64(
      testNow.add(const Duration(minutes: 15)).millisecondsSinceEpoch,
    ),
    refreshExpiresAtUnixMs: Int64(
      testNow.add(const Duration(days: 1)).millisecondsSinceEpoch,
    ),
    sessionId: 'session-a',
    ownerId: 'owner-a',
    actorId: 'actor-a',
  );

  @override
  GrpcUnaryResponse<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  ) {
    bootstrapRequest = request;
    bootstrapOptions = options;
    if (bootstrapError case final error?) {
      return _FakeGrpcUnaryResponse(Future.error(error));
    }
    return _FakeGrpcUnaryResponse(Future.value(sessionReply));
  }

  @override
  GrpcUnaryResponse<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  ) {
    refreshRequest = request;
    refreshOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(sessionReply));
  }

  @override
  GrpcUnaryResponse<wire.LogoutSessionReply> logoutSession(
    wire.LogoutSessionRequest request,
    CallOptions options,
  ) {
    logoutRequest = request;
    logoutOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.LogoutSessionReply()));
  }

  @override
  GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  ) {
    watchRequest = request;
    watchOptions = options;
    return feedResponse ?? _FakeGrpcFeedResponse(const Stream.empty());
  }

  @override
  GrpcUnaryResponse<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  ) {
    ackRequest = request;
    ackOptions = options;
    return ackResponse ??
        _FakeGrpcUnaryResponse(
          Future.value(
            wire.AcknowledgeSurfaceFeedReply(
              acknowledgedSequence: request.sequence,
            ),
          ),
        );
  }

  @override
  GrpcUnaryResponse<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  ) {
    actionRequest = request;
    actionOptions = options;
    if (actionError case final error?) {
      return _FakeGrpcUnaryResponse(Future.error(error));
    }
    return _FakeGrpcUnaryResponse(
      Future.value(
        wire.SubmitActionReply(
          operationId: 'operation-a',
          idempotencyKey: 'idempotency-a',
        ),
      ),
    );
  }
}

class _FakeGrpcUnaryResponse<T> implements GrpcUnaryResponse<T> {
  _FakeGrpcUnaryResponse(this.response, {this.onCancel});

  @override
  final Future<T> response;
  final Future<void> Function()? onCancel;
  bool cancelled = false;

  @override
  Future<void> cancel() async {
    cancelled = true;
    await onCancel?.call();
  }
}

class _FakeGrpcFeedResponse implements GrpcFeedResponse {
  _FakeGrpcFeedResponse(this.events);

  @override
  final Stream<wire.SurfaceFeedEvent> events;
  bool cancelled = false;

  @override
  Future<void> cancel() async {
    cancelled = true;
  }
}
