import 'dart:async';

import 'package:digitalbrain_flutter/core/session/digitalbrain_client.dart';
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
      'login exchanges development credentials for the exact UI audience',
      () async {
        expect(digitalBrainUiAudience, 'digitalbrain-v3-ui');
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        final session = await transport.login(
          username: 'admin',
          password: 'admin',
        );

        expect(port.bootstrapRequest?.username, 'admin');
        expect(port.bootstrapRequest?.password, 'admin');
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
      'external login sends bearer identity and an empty credential body',
      () async {
        const identityToken =
            'identityheader.identitypayload.identitysignature';
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        final session = await transport.loginExternal(identityToken);

        expect(port.bootstrapRequest?.username, isEmpty);
        expect(port.bootstrapRequest?.password, isEmpty);
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

    test('external login rejects malformed compact identity tokens', () async {
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
          transport.loginExternal(token),
          throwsA(isA<AuthenticationException>()),
        );
      }

      expect(port.bootstrapRequest, isNull);
      expect(port.bootstrapOptions, isNull);
    });

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

    test(
      'feature draft load uses the signed session and ten second deadline',
      () async {
        final reply = wire.FeatureDraftReply();
        final port = _FakeGrpcClientPort()..featureDraftReply = reply;
        final transport = GrpcUiTransport.forTesting(client: port);
        final request = wire.GetFeatureDraftRequest(draftId: 'draft-a');

        final result = await transport.getFeatureDraft(
          accessToken: 'signed-session',
          request: request,
        );

        expect(result, same(reply));
        expect(port.getFeatureDraftRequest, same(request));
        expect(port.getFeatureDraftOptions?.metadata, {
          'x-v2-session': 'signed-session',
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(port.getFeatureDraftOptions?.timeout, unaryRequestTimeout);
      },
    );

    test(
      'pending install reset is a signed cancellable authority call',
      () async {
        final reply = wire.FeatureDraftReply();
        final port = _FakeGrpcClientPort()..featureDraftReply = reply;
        final transport = GrpcUiTransport.forTesting(client: port);
        final request = wire.ResetFeatureDraftInstallationRequest(
          draftId: 'draft-a',
          idempotencyId: 'reset-a',
        );

        final result = await transport.resetFeatureDraftInstallation(
          accessToken: 'signed-session',
          request: request,
        );

        expect(result, same(reply));
        expect(port.resetFeatureDraftInstallationRequest, same(request));
        expect(port.resetFeatureDraftInstallationOptions?.metadata, {
          'x-v2-session': 'signed-session',
          'x-v2-audience': digitalBrainUiAudience,
        });
        expect(
          port.resetFeatureDraftInstallationOptions?.timeout,
          featureAuthorityRequestTimeout,
        );
      },
    );

    test('pending install reset preserves caller-reserved protobuf tags', () {
      final request = wire.ResetFeatureDraftInstallationRequest(
        draftId: 'a',
        idempotencyId: 'b',
      );

      expect(request.writeToBuffer(), [26, 1, 97, 34, 1, 98]);
    });

    test('originating request resume is a signed cancellable call', () async {
      final reply = wire.ResumeOriginatingRequestReply();
      final port = _FakeGrpcClientPort()..resumeOriginatingRequestReply = reply;
      final transport = GrpcUiTransport.forTesting(client: port);
      final request = wire.ResumeOriginatingRequestRequest(
        draftId: 'draft-a',
        expectedRevision: Int64(6),
        idempotencyId: 'run-a',
      );

      final result = await transport.resumeOriginatingRequest(
        accessToken: 'signed-session',
        request: request,
      );

      expect(result, same(reply));
      expect(port.resumeOriginatingRequestRequest, same(request));
      expect(port.resumeOriginatingRequestOptions?.metadata, {
        'x-v2-session': 'signed-session',
        'x-v2-audience': digitalBrainUiAudience,
      });
      expect(
        port.resumeOriginatingRequestOptions?.timeout,
        unaryRequestTimeout,
      );
    });

    test(
      'feature draft mutations use bounded authenticated deadlines',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);
        final revision = wire.ReviseFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'revision-a',
        );
        final suggestion = wire.SuggestFeatureChangeRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          guidance: 'Make the expected outcome measurable.',
          suggestionId: 'suggestion-a',
        );
        final verification = wire.VerifyFeatureDraftRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(4),
          idempotencyId: 'verification-a',
        );

        await transport.reviseFeatureDraft(
          accessToken: 'signed-session',
          request: revision,
        );
        await transport.suggestFeatureChange(
          accessToken: 'signed-session',
          request: suggestion,
        );
        await transport.verifyFeatureDraft(
          accessToken: 'signed-session',
          request: verification,
        );

        expect(port.reviseFeatureDraftRequest, same(revision));
        expect(port.suggestFeatureChangeRequest, same(suggestion));
        expect(port.verifyFeatureDraftRequest, same(verification));
        for (final options in [
          port.reviseFeatureDraftOptions,
          port.suggestFeatureChangeOptions,
          port.verifyFeatureDraftOptions,
        ]) {
          expect(options?.metadata, {
            'x-v2-session': 'signed-session',
            'x-v2-audience': digitalBrainUiAudience,
          });
        }
        expect(port.reviseFeatureDraftOptions?.timeout, unaryRequestTimeout);
        expect(
          port.suggestFeatureChangeOptions?.timeout,
          featureSuggestionRequestTimeout,
        );
        expect(
          port.verifyFeatureDraftOptions?.timeout,
          featureVerificationRequestTimeout,
        );
        expect(
          featureSuggestionRequestTimeout,
          lessThanOrEqualTo(const Duration(seconds: 70)),
        );
        expect(
          featureVerificationRequestTimeout,
          lessThanOrEqualTo(const Duration(seconds: 70)),
        );
      },
    );

    test(
      'governed review install detail source and rollback use signed bounded calls',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);
        final review = wire.ReviewFeatureAccessRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(5),
          installationId: 'installation-a',
          releaseDigest: List.filled(64, 'a').join(),
          subscriptions: const ['manual'],
        );
        final install = wire.InstallFeatureVersionRequest(
          draftId: 'draft-a',
          expectedRevision: Int64(5),
          installationId: 'installation-a',
          releaseDigest: List.filled(64, 'a').join(),
          subscriptions: const ['manual'],
          decisionId: 'decision-a',
          idempotencyId: 'install-a',
        );
        final detail = wire.GetFeatureRequest(featureId: 'draft-a');
        final source = wire.GetFeatureReleaseSourceRequest(
          featureId: 'draft-a',
          installationId: 'installation-a',
          releaseDigest: List.filled(64, 'a').join(),
          sourceReference: 'sha256:${List.filled(64, 'c').join()}',
        );
        final rollback = wire.RollbackFeatureVersionRequest(
          featureId: 'draft-a',
          expectedActiveDigest: List.filled(64, 'b').join(),
          targetDigest: List.filled(64, 'a').join(),
          idempotencyId: 'rollback-a',
          expectedRevision: Int64(6),
        );

        await transport.reviewFeatureAccess(
          accessToken: 'signed-session',
          request: review,
        );
        await transport.installFeatureVersion(
          accessToken: 'signed-session',
          request: install,
        );
        await transport.getFeature(
          accessToken: 'signed-session',
          request: detail,
        );
        await transport.getFeatureReleaseSource(
          accessToken: 'signed-session',
          request: source,
        );
        await transport.rollbackFeatureVersion(
          accessToken: 'signed-session',
          request: rollback,
        );

        expect(port.reviewFeatureAccessRequest, same(review));
        expect(port.installFeatureVersionRequest, same(install));
        expect(port.getFeatureRequest, same(detail));
        expect(port.getFeatureReleaseSourceRequest, same(source));
        expect(port.rollbackFeatureVersionRequest, same(rollback));
        for (final options in [
          port.reviewFeatureAccessOptions,
          port.installFeatureVersionOptions,
          port.getFeatureOptions,
          port.getFeatureReleaseSourceOptions,
          port.rollbackFeatureVersionOptions,
        ]) {
          expect(options?.metadata, {
            'x-v2-session': 'signed-session',
            'x-v2-audience': digitalBrainUiAudience,
          });
        }
        expect(port.reviewFeatureAccessOptions?.timeout, unaryRequestTimeout);
        expect(
          port.installFeatureVersionOptions?.timeout,
          featureAuthorityRequestTimeout,
        );
        expect(port.getFeatureOptions?.timeout, unaryRequestTimeout);
        expect(
          port.getFeatureReleaseSourceOptions?.timeout,
          unaryRequestTimeout,
        );
        expect(
          port.rollbackFeatureVersionOptions?.timeout,
          featureAuthorityRequestTimeout,
        );
      },
    );

    test(
      'Activity, Run, and Chat context queries use signed bounded calls',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);
        final list = wire.ListActivityRequest(
          featureId: 'feature-a',
          limit: 100,
        );
        final run = wire.GetRunRequest(runId: 'run-a');
        final context = wire.GetConversationContextRequest(
          conversationId: 'conversation-a',
          requestId: 'request-a',
        );

        await transport.listActivity(
          accessToken: 'signed-session',
          request: list,
        );
        await transport.getRun(accessToken: 'signed-session', request: run);
        await transport.getConversationContext(
          accessToken: 'signed-session',
          request: context,
        );

        expect(port.listActivityRequest, same(list));
        expect(port.getRunRequest, same(run));
        expect(port.contextRequest, same(context));
        for (final options in [
          port.listActivityOptions,
          port.getRunOptions,
          port.contextOptions,
        ]) {
          expect(options?.metadata, {
            'x-v2-session': 'signed-session',
            'x-v2-audience': digitalBrainUiAudience,
          });
          expect(options?.timeout, unaryRequestTimeout);
        }
      },
    );

    test(
      'preserves safe draft not found and revision conflict codes',
      () async {
        final port = _FakeGrpcClientPort();
        final transport = GrpcUiTransport.forTesting(client: port);

        port.getFeatureDraftError = GrpcError.notFound(
          'owner-a/draft-secret must not escape',
        );
        await expectLater(
          transport.getFeatureDraft(
            accessToken: 'signed-session',
            request: wire.GetFeatureDraftRequest(draftId: 'draft-a'),
          ),
          throwsA(
            isA<TransportException>()
                .having(
                  (error) => error.code,
                  'code',
                  TransportErrorCode.notFound,
                )
                .having(
                  (error) => error.safeMessage,
                  'safeMessage',
                  'Draft was not found.',
                ),
          ),
        );

        port.getFeatureDraftError = GrpcError.aborted(
          'expected revision 4 but found 9 must not escape',
        );
        await expectLater(
          transport.getFeatureDraft(
            accessToken: 'signed-session',
            request: wire.GetFeatureDraftRequest(draftId: 'draft-a'),
          ),
          throwsA(
            isA<TransportException>()
                .having(
                  (error) => error.code,
                  'code',
                  TransportErrorCode.aborted,
                )
                .having(
                  (error) => error.safeMessage,
                  'safeMessage',
                  'Draft changed on the server.',
                ),
          ),
        );
      },
    );

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
          isA<PreconditionException>()
              .having(
                (error) => error.code,
                'code',
                TransportErrorCode.failedPrecondition,
              )
              .having(
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
      'sign out cancels product calls before logout and rejects late results',
      () async {
        final revisePending = Completer<wire.FeatureDraftReply>();
        final suggestPending = Completer<wire.FeatureDraftPatchReply>();
        final verifyPending = Completer<wire.FeatureReleaseReviewReply>();
        final port = _FakeGrpcClientPort();
        final reviseResponse = _FakeGrpcUnaryResponse(
          revisePending.future,
          onCancel: () async => port.events.add('cancel-revise'),
        );
        final suggestResponse = _FakeGrpcUnaryResponse(
          suggestPending.future,
          onCancel: () async => port.events.add('cancel-suggest'),
        );
        final verifyResponse = _FakeGrpcUnaryResponse(
          verifyPending.future,
          onCancel: () async => port.events.add('cancel-verify'),
        );
        port
          ..reviseFeatureDraftResponse = reviseResponse
          ..suggestFeatureChangeResponse = suggestResponse
          ..verifyFeatureDraftResponse = verifyResponse;
        var closeCalls = 0;
        final transport = GrpcUiTransport.forTesting(
          client: port,
          close: () async {
            closeCalls++;
          },
        );
        final session = SessionController(now: () => testNow)
          ..establish(testSession());
        final revise = transport.reviseFeatureDraft(
          accessToken: 'access-token',
          request: wire.ReviseFeatureDraftRequest(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'mutation-a',
          ),
        );
        final suggest = transport.suggestFeatureChange(
          accessToken: 'access-token',
          request: wire.SuggestFeatureChangeRequest(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            guidance: 'Make the outcome measurable.',
            suggestionId: 'suggestion-a',
          ),
        );
        final verify = transport.verifyFeatureDraft(
          accessToken: 'access-token',
          request: wire.VerifyFeatureDraftRequest(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'verification-a',
          ),
        );
        final cancellation = isA<TransportException>().having(
          (error) => error.code,
          'code',
          TransportErrorCode.cancelled,
        );
        final expectations = [
          expectLater(revise, throwsA(cancellation)),
          expectLater(suggest, throwsA(cancellation)),
          expectLater(verify, throwsA(cancellation)),
        ];
        await Future<void>.delayed(Duration.zero);

        await session.signOut(transport);
        revisePending.complete(wire.FeatureDraftReply());
        suggestPending.complete(wire.FeatureDraftPatchReply());
        verifyPending.complete(wire.FeatureReleaseReviewReply());
        await Future.wait(expectations);
        expect(port.events, [
          'cancel-revise',
          'cancel-suggest',
          'cancel-verify',
          'logout',
        ]);
        expect(reviseResponse.cancelled, isTrue);
        expect(suggestResponse.cancelled, isTrue);
        expect(verifyResponse.cancelled, isTrue);
        expect(closeCalls, 0);
      },
    );

    test(
      'sign out maps a late product authentication error to cancellation',
      () async {
        final pending = Completer<wire.FeatureDraftReply>();
        final response = _FakeGrpcUnaryResponse(pending.future);
        final port = _FakeGrpcClientPort()
          ..reviseFeatureDraftResponse = response;
        final transport = GrpcUiTransport.forTesting(client: port);
        final session = SessionController(now: () => testNow)
          ..establish(testSession());
        var authenticationRequiredCalls = 0;
        final client = DigitalBrainClient(
          session: session,
          transport: transport,
          onAuthenticationRequired: () async {
            authenticationRequiredCalls++;
          },
        );

        final revision = client.reviseFeatureDraft(
          wire.ReviseFeatureDraftRequest(
            draftId: 'draft-a',
            expectedRevision: Int64(4),
            idempotencyId: 'mutation-a',
          ),
        );
        await Future<void>.delayed(Duration.zero);
        await session.signOut(transport);
        pending.completeError(GrpcError.unauthenticated('late rejection'));

        await expectLater(
          revision,
          throwsA(
            isA<TransportException>().having(
              (error) => error.code,
              'code',
              TransportErrorCode.cancelled,
            ),
          ),
        );
        expect(authenticationRequiredCalls, 0);
        expect(session.status, SessionStatus.signedOut);
      },
    );

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

      await runtime.authenticateWithPassword(
        username: 'admin',
        password: 'admin',
      );
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
          await transport.login(username: 'admin', password: 'bad');
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
  final List<String> events = [];
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
  wire.GetFeatureDraftRequest? getFeatureDraftRequest;
  CallOptions? getFeatureDraftOptions;
  wire.ResetFeatureDraftInstallationRequest?
  resetFeatureDraftInstallationRequest;
  CallOptions? resetFeatureDraftInstallationOptions;
  wire.FeatureDraftReply? featureDraftReply;
  Object? getFeatureDraftError;
  wire.ReviseFeatureDraftRequest? reviseFeatureDraftRequest;
  CallOptions? reviseFeatureDraftOptions;
  GrpcUnaryResponse<wire.FeatureDraftReply>? reviseFeatureDraftResponse;
  wire.SuggestFeatureChangeRequest? suggestFeatureChangeRequest;
  CallOptions? suggestFeatureChangeOptions;
  GrpcUnaryResponse<wire.FeatureDraftPatchReply>? suggestFeatureChangeResponse;
  wire.VerifyFeatureDraftRequest? verifyFeatureDraftRequest;
  CallOptions? verifyFeatureDraftOptions;
  GrpcUnaryResponse<wire.FeatureReleaseReviewReply>? verifyFeatureDraftResponse;
  wire.ReviewFeatureAccessRequest? reviewFeatureAccessRequest;
  CallOptions? reviewFeatureAccessOptions;
  wire.InstallFeatureVersionRequest? installFeatureVersionRequest;
  CallOptions? installFeatureVersionOptions;
  wire.ResumeOriginatingRequestRequest? resumeOriginatingRequestRequest;
  CallOptions? resumeOriginatingRequestOptions;
  wire.ResumeOriginatingRequestReply? resumeOriginatingRequestReply;
  wire.GetFeatureRequest? getFeatureRequest;
  CallOptions? getFeatureOptions;
  wire.GetFeatureReleaseSourceRequest? getFeatureReleaseSourceRequest;
  CallOptions? getFeatureReleaseSourceOptions;
  wire.RollbackFeatureVersionRequest? rollbackFeatureVersionRequest;
  CallOptions? rollbackFeatureVersionOptions;
  wire.ListActivityRequest? listActivityRequest;
  CallOptions? listActivityOptions;
  wire.GetRunRequest? getRunRequest;
  CallOptions? getRunOptions;
  wire.GetConversationContextRequest? contextRequest;
  CallOptions? contextOptions;

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
    events.add('logout');
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

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> getFeatureDraft(
    wire.GetFeatureDraftRequest request,
    CallOptions options,
  ) {
    getFeatureDraftRequest = request;
    getFeatureDraftOptions = options;
    if (getFeatureDraftError case final error?) {
      return _FakeGrpcUnaryResponse(Future.error(error));
    }
    return _FakeGrpcUnaryResponse(
      Future.value(featureDraftReply ?? wire.FeatureDraftReply()),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> resetFeatureDraftInstallation(
    wire.ResetFeatureDraftInstallationRequest request,
    CallOptions options,
  ) {
    resetFeatureDraftInstallationRequest = request;
    resetFeatureDraftInstallationOptions = options;
    return _FakeGrpcUnaryResponse(
      Future.value(featureDraftReply ?? wire.FeatureDraftReply()),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureDraftReply> reviseFeatureDraft(
    wire.ReviseFeatureDraftRequest request,
    CallOptions options,
  ) {
    reviseFeatureDraftRequest = request;
    reviseFeatureDraftOptions = options;
    final response = reviseFeatureDraftResponse;
    if (response != null) return response;
    return _FakeGrpcUnaryResponse(Future.value(wire.FeatureDraftReply()));
  }

  @override
  GrpcUnaryResponse<wire.FeatureDraftPatchReply> suggestFeatureChange(
    wire.SuggestFeatureChangeRequest request,
    CallOptions options,
  ) {
    suggestFeatureChangeRequest = request;
    suggestFeatureChangeOptions = options;
    final response = suggestFeatureChangeResponse;
    if (response != null) return response;
    return _FakeGrpcUnaryResponse(Future.value(wire.FeatureDraftPatchReply()));
  }

  @override
  GrpcUnaryResponse<wire.FeatureReleaseReviewReply> verifyFeatureDraft(
    wire.VerifyFeatureDraftRequest request,
    CallOptions options,
  ) {
    verifyFeatureDraftRequest = request;
    verifyFeatureDraftOptions = options;
    final response = verifyFeatureDraftResponse;
    if (response != null) return response;
    return _FakeGrpcUnaryResponse(
      Future.value(wire.FeatureReleaseReviewReply()),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureAccessReviewReply> reviewFeatureAccess(
    wire.ReviewFeatureAccessRequest request,
    CallOptions options,
  ) {
    reviewFeatureAccessRequest = request;
    reviewFeatureAccessOptions = options;
    return _FakeGrpcUnaryResponse(
      Future.value(wire.FeatureAccessReviewReply()),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureInstallReply> installFeatureVersion(
    wire.InstallFeatureVersionRequest request,
    CallOptions options,
  ) {
    installFeatureVersionRequest = request;
    installFeatureVersionOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.FeatureInstallReply()));
  }

  @override
  GrpcUnaryResponse<wire.ResumeOriginatingRequestReply>
  resumeOriginatingRequest(
    wire.ResumeOriginatingRequestRequest request,
    CallOptions options,
  ) {
    resumeOriginatingRequestRequest = request;
    resumeOriginatingRequestOptions = options;
    return _FakeGrpcUnaryResponse(
      Future.value(
        resumeOriginatingRequestReply ?? wire.ResumeOriginatingRequestReply(),
      ),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureReply> getFeature(
    wire.GetFeatureRequest request,
    CallOptions options,
  ) {
    getFeatureRequest = request;
    getFeatureOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.FeatureReply()));
  }

  @override
  GrpcUnaryResponse<wire.FeatureReleaseSourceReply> getFeatureReleaseSource(
    wire.GetFeatureReleaseSourceRequest request,
    CallOptions options,
  ) {
    getFeatureReleaseSourceRequest = request;
    getFeatureReleaseSourceOptions = options;
    return _FakeGrpcUnaryResponse(
      Future.value(wire.FeatureReleaseSourceReply()),
    );
  }

  @override
  GrpcUnaryResponse<wire.FeatureReply> rollbackFeatureVersion(
    wire.RollbackFeatureVersionRequest request,
    CallOptions options,
  ) {
    rollbackFeatureVersionRequest = request;
    rollbackFeatureVersionOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.FeatureReply()));
  }

  @override
  GrpcUnaryResponse<wire.ListActivityReply> listActivity(
    wire.ListActivityRequest request,
    CallOptions options,
  ) {
    listActivityRequest = request;
    listActivityOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.ListActivityReply()));
  }

  @override
  GrpcUnaryResponse<wire.RunReply> getRun(
    wire.GetRunRequest request,
    CallOptions options,
  ) {
    getRunRequest = request;
    getRunOptions = options;
    return _FakeGrpcUnaryResponse(Future.value(wire.RunReply()));
  }

  @override
  GrpcUnaryResponse<wire.GetConversationContextReply> getConversationContext(
    wire.GetConversationContextRequest request,
    CallOptions options,
  ) {
    contextRequest = request;
    contextOptions = options;
    return _FakeGrpcUnaryResponse(
      Future.value(wire.GetConversationContextReply()),
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
