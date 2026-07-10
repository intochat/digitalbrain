import 'dart:async';

import 'package:digitalbrain_flutter/grpc/v2_ui.pb.dart' as wire;
import 'package:digitalbrain_flutter/v2/v2_config.dart';
import 'package:digitalbrain_flutter/v2/v2_grpc_transport.dart';
import 'package:digitalbrain_flutter/v2/v2_runtime.dart';
import 'package:fixnum/fixnum.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:grpc/grpc.dart';

import 'v2_test_fixtures.dart';

void main() {
  group('V2GrpcUiTransport', () {
    test(
      'production channel disables metadata-bearing timeline logging',
      () async {
        isTimelineLoggingEnabled = true;
        final transport = V2GrpcUiTransport.connect(
          Uri.parse('https://localhost:7443'),
        );

        expect(isTimelineLoggingEnabled, isFalse);
        await transport.close();
      },
    );

    test('production channel refuses a plaintext endpoint', () {
      expect(
        () => V2GrpcUiTransport.connect(Uri.parse('http://localhost:5080')),
        throwsArgumentError,
      );
    });

    test(
      'bootstrap exchanges the credential for the exact UI audience',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = V2GrpcUiTransport.forTesting(client: port);

        final session = await transport.bootstrapSession('bootstrap-once');

        expect(port.bootstrapRequest?.secret, 'bootstrap-once');
        expect(port.bootstrapOptions?.metadata, {
          'x-v2-audience': digitalBrainV2UiAudience,
        });
        expect(
          port.bootstrapOptions?.metadata,
          isNot(contains('x-v2-session')),
        );
        expect(session.identity.workspaceId, 'workspace-a');
        expect(session.credentials.accessToken, 'access-token');
      },
    );

    test(
      'refresh sends exact audience and never requires expired access',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = V2GrpcUiTransport.forTesting(client: port);

        await transport.refreshSession(refreshToken: 'refresh-opaque');

        expect(port.refreshRequest?.refreshToken, 'refresh-opaque');
        expect(port.refreshOptions?.metadata, {
          'x-v2-audience': digitalBrainV2UiAudience,
        });
        expect(port.refreshOptions?.metadata, isNot(contains('x-v2-session')));
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
        final transport = V2GrpcUiTransport.forTesting(client: port);

        final call = await transport.watchSurfaceFeed(
          accessToken: 'signed-session',
          afterSequence: 7,
          audience: V2FeedAudience.principal,
          clientCapabilities: const {'ui.payload.native', 'ui.protocol.v2'},
          maxBatchSize: 25,
        );
        final events = await call.events.toList();

        expect(port.watchRequest?.afterSequence.toInt(), 7);
        expect(
          port.watchRequest?.audience,
          wire.FeedAudienceKind.FEED_AUDIENCE_KIND_PRINCIPAL,
        );
        expect(port.watchRequest?.clientCapabilities, [
          'ui.payload.native',
          'ui.protocol.v2',
        ]);
        expect(port.watchRequest?.maxBatchSize, 25);
        expect(port.watchOptions?.metadata, {
          'x-v2-session': 'signed-session',
          'x-v2-audience': digitalBrainV2UiAudience,
        });
        expect(events.first, isA<V2FeedSurfaceJson>());
        final reset = events.last as V2FeedResetEvent;
        expect(reset.reason, 'retention-gap');
        expect(reset.resumeSequence, 12);
        expect(reset.snapshotJson, hasLength(1));

        await call.cancel();
        expect(response.cancelled, isTrue);
      },
    );

    test('acknowledgement and action use signed session metadata', () async {
      final port = _FakeGrpcClientPort();
      final transport = V2GrpcUiTransport.forTesting(client: port);
      final surface = testSurface(actions: [testActionJson()]);

      await transport.acknowledgeSurfaceFeed(
        accessToken: 'signed-session',
        audience: V2FeedAudience.principal,
        sequence: 4,
      );
      final result = await transport.submitAction(
        accessToken: 'signed-session',
        action: surface.actions.single,
        input: const {'confirmed': true},
      );

      expect(port.ackOptions?.metadata, {
        'x-v2-session': 'signed-session',
        'x-v2-audience': digitalBrainV2UiAudience,
      });
      expect(port.actionOptions?.metadata, port.ackOptions?.metadata);
      expect(port.actionRequest?.bindingId, 'refresh-binding');
      expect(port.actionRequest?.actionToken, 'signed-action-token');
      expect(port.actionRequest?.surfaceRevision, 1);
      expect(port.actionRequest?.inputJson, '{"confirmed":true}');
      expect(result.operationId, 'operation-a');
    });

    test(
      'rejects anonymous calls and private fields in action input',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = V2GrpcUiTransport.forTesting(client: port);
        final action = testSurface(actions: [testActionJson()]).actions.single;

        await expectLater(
          transport.watchSurfaceFeed(
            accessToken: '',
            afterSequence: 0,
            audience: V2FeedAudience.principal,
            clientCapabilities: const {},
            maxBatchSize: 1,
          ),
          throwsA(isA<V2AuthenticationException>()),
        );
        await expectLater(
          transport.submitAction(
            accessToken: 'signed-session',
            action: action,
            input: const {'workspaceId': 'must-not-travel'},
          ),
          throwsA(isA<V2ProtocolException>()),
        );
        expect(port.actionRequest, isNull);
      },
    );

    test(
      'maps authentication errors without retaining server details',
      () async {
        final port = _FakeGrpcClientPort()
          ..bootstrapError = GrpcError.unauthenticated(
            'access_token=must-not-escape',
          );
        final transport = V2GrpcUiTransport.forTesting(client: port);

        Object? caught;
        try {
          await transport.bootstrapSession('bad');
        } catch (error) {
          caught = error;
        }

        expect(caught, isA<V2AuthenticationException>());
        expect(caught.toString(), isNot(contains('must-not-escape')));
        expect(caught.toString(), isNot(contains('access_token')));
      },
    );
  });
}

class _FakeGrpcClientPort implements V2GrpcClientPort {
  wire.BootstrapSessionRequest? bootstrapRequest;
  CallOptions? bootstrapOptions;
  wire.RefreshSessionRequest? refreshRequest;
  CallOptions? refreshOptions;
  wire.WatchSurfaceFeedRequest? watchRequest;
  CallOptions? watchOptions;
  wire.AcknowledgeSurfaceFeedRequest? ackRequest;
  CallOptions? ackOptions;
  wire.SubmitActionRequest? actionRequest;
  CallOptions? actionOptions;
  Object? bootstrapError;
  V2GrpcFeedResponse? feedResponse;

  wire.SessionReply get sessionReply => wire.SessionReply(
    accessToken: 'access-token',
    refreshToken: 'refresh-token',
    accessExpiresAtUnixMs: Int64(
      v2TestNow.add(const Duration(minutes: 15)).millisecondsSinceEpoch,
    ),
    refreshExpiresAtUnixMs: Int64(
      v2TestNow.add(const Duration(days: 1)).millisecondsSinceEpoch,
    ),
    sessionId: 'session-a',
    tenantId: 'tenant-a',
    workspaceId: 'workspace-a',
    principalId: 'principal-a',
  );

  @override
  Future<wire.SessionReply> bootstrapSession(
    wire.BootstrapSessionRequest request,
    CallOptions options,
  ) async {
    bootstrapRequest = request;
    bootstrapOptions = options;
    if (bootstrapError case final error?) throw error;
    return sessionReply;
  }

  @override
  Future<wire.SessionReply> refreshSession(
    wire.RefreshSessionRequest request,
    CallOptions options,
  ) async {
    refreshRequest = request;
    refreshOptions = options;
    return sessionReply;
  }

  @override
  V2GrpcFeedResponse watchSurfaceFeed(
    wire.WatchSurfaceFeedRequest request,
    CallOptions options,
  ) {
    watchRequest = request;
    watchOptions = options;
    return feedResponse ?? _FakeGrpcFeedResponse(const Stream.empty());
  }

  @override
  Future<wire.AcknowledgeSurfaceFeedReply> acknowledgeSurfaceFeed(
    wire.AcknowledgeSurfaceFeedRequest request,
    CallOptions options,
  ) async {
    ackRequest = request;
    ackOptions = options;
    return wire.AcknowledgeSurfaceFeedReply(
      acknowledgedSequence: request.sequence,
    );
  }

  @override
  Future<wire.SubmitActionReply> submitAction(
    wire.SubmitActionRequest request,
    CallOptions options,
  ) async {
    actionRequest = request;
    actionOptions = options;
    return wire.SubmitActionReply(
      operationId: 'operation-a',
      idempotencyKey: 'idempotency-a',
    );
  }
}

class _FakeGrpcFeedResponse implements V2GrpcFeedResponse {
  _FakeGrpcFeedResponse(this.events);

  @override
  final Stream<wire.SurfaceFeedEvent> events;
  bool cancelled = false;

  @override
  Future<void> cancel() async {
    cancelled = true;
  }
}
