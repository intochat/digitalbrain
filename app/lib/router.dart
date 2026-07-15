import 'dart:async';

import 'package:fixnum/fixnum.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'core/session/app_session_scope.dart';
import 'features/activity/activity_gateway.dart';
import 'features/activity/activity_page.dart';
import 'features/activity/activity_run_page.dart';
import 'features/releases/feature_release_gateway.dart';
import 'features/releases/feature_release_page.dart';
import 'features/studio/feature_studio_gateway.dart';
import 'features/studio/feature_studio_page.dart';
import 'runtime/widgets/chat_page.dart';
import 'runtime/widgets/runtime_shell.dart';
import 'shell/digitalbrain_shell.dart';

enum _FeatureVersionArrival { restored }

GoRouter createDigitalBrainRouter({
  String initialLocation = '/chat',
  String Function()? runNowIdFactory,
}) {
  GoRouter.optionURLReflectsImperativeAPIs = true;
  final studioExit = FeatureStudioExitCoordinator();
  var runNowSequence = 0;
  final nextRunNowId =
      runNowIdFactory ??
      () =>
          'run-now-${DateTime.now().toUtc().microsecondsSinceEpoch}-${++runNowSequence}';
  Widget buildFeatureVersion(BuildContext context, GoRouterState state) {
    final featureId = state.pathParameters['featureId']!;
    final releaseDigest = state.pathParameters['releaseDigest']!;
    final client = AppSessionScope.of(context).digitalBrainClient;
    if (client == null) {
      return const Material(
        child: Center(child: Text('Feature is unavailable.')),
      );
    }
    return FeatureReleasePage(
      key: ValueKey('feature-version-$featureId-$releaseDigest'),
      featureId: featureId,
      expectedReleaseDigest: releaseDigest,
      onVersionRestored: (restoredDigest) => context.replaceNamed(
        'feature-version',
        pathParameters: {
          'featureId': featureId,
          'releaseDigest': restoredDigest,
        },
        extra: _FeatureVersionArrival.restored,
      ),
      restoredOnArrival: state.extra == _FeatureVersionArrival.restored,
      gateway: GrpcFeatureReleaseGateway(client: client),
    );
  }

  return GoRouter(
    initialLocation: initialLocation,
    routes: [
      GoRoute(path: '/', redirect: (context, state) => '/chat'),
      ShellRoute(
        builder: (context, state, child) => RuntimeShell(
          child: DigitalBrainShell(
            location: state.uri,
            onDestinationSelected: (destination) {
              if (destination.location == state.uri.path) return;
              if (studioExit.isAttached && destination.location == '/chat') {
                unawaited(studioExit.requestExit());
                return;
              }
              context.go(destination.location);
            },
            onSignOut: AppSessionScope.of(context).signOut,
            child: child,
          ),
        ),
        routes: [
          GoRoute(
            path: '/chat',
            name: 'chat',
            builder: (context, state) {
              final activityReference = ChatActivityReference.tryCreate(
                conversationId: state.uri.queryParameters['conversationId'],
                requestId: state.uri.queryParameters['requestId'],
              );
              if (state.uri.queryParameters['intent'] !=
                  'resume-originating-request') {
                return ChatPage(activityReference: activityReference);
              }
              final intent = _resumeIntent(state.uri);
              return ChatPage(
                resumeIntent: intent,
                invalidResumeIntent: intent == null,
                activityReference: activityReference,
              );
            },
          ),
          GoRoute(
            path: '/activity',
            name: 'activity',
            builder: (context, state) {
              final client = AppSessionScope.of(context).digitalBrainClient;
              if (client == null) {
                return const Material(
                  child: Center(child: Text('Activity is unavailable.')),
                );
              }
              return ActivityPage(
                gateway: GrpcActivityGateway(client: client),
                onRunSelected: (run) => context.pushNamed(
                  'activity-run',
                  pathParameters: {'runId': run.runId},
                ),
                onOpenFeature: (featureId) => context.goNamed(
                  'feature-release',
                  pathParameters: {'featureId': featureId},
                ),
                onOpenConversation: (conversationId) => context.go(
                  _activityChatLocation(conversationId: conversationId),
                ),
                onOpenRequest: (requestId) =>
                    context.go(_activityChatLocation(requestId: requestId)),
                onOpenAutomation: (featureId, automationId) => context.go(
                  _featureAutomationLocation(
                    featureId: featureId,
                    automationId: automationId,
                  ),
                ),
              );
            },
          ),
          GoRoute(
            path: '/activity/:runId',
            name: 'activity-run',
            builder: (context, state) {
              final client = AppSessionScope.of(context).digitalBrainClient;
              if (client == null) {
                return const Material(
                  child: Center(child: Text('Run details are unavailable.')),
                );
              }
              return ActivityRunPage(
                key: ValueKey('activity-run-${state.pathParameters['runId']}'),
                runId: state.pathParameters['runId']!,
                gateway: GrpcActivityGateway(client: client),
                sessionIdentity: client,
                onBackToActivity: () {
                  if (context.canPop()) {
                    context.pop();
                    return;
                  }
                  context.goNamed('activity');
                },
                onOpenFeature: (featureId) => context.goNamed(
                  'feature-release',
                  pathParameters: {'featureId': featureId},
                ),
                onOpenConversation: (conversationId) => context.go(
                  _activityChatLocation(conversationId: conversationId),
                ),
                onOpenRequest: (requestId) =>
                    context.go(_activityChatLocation(requestId: requestId)),
                onOpenAutomation: (featureId, automationId) => context.go(
                  _featureAutomationLocation(
                    featureId: featureId,
                    automationId: automationId,
                  ),
                ),
              );
            },
          ),
          GoRoute(
            path: '/features/proposals/:proposalId',
            name: 'feature-proposal',
            onExit: (context, state) {
              if (!studioExit.isAttached) return true;
              return studioExit.requestExit(navigate: false);
            },
            builder: (context, state) {
              final draftId = state.pathParameters['proposalId']!;
              final installationId =
                  state.uri.queryParameters['installationId'];
              final client = AppSessionScope.of(context).digitalBrainClient;
              if (client == null) {
                return const Material(
                  child: Center(child: Text('Feature Studio is unavailable.')),
                );
              }
              return FeatureStudioPage(
                key: ValueKey('feature-studio-$draftId-$installationId'),
                draftId: draftId,
                requestedInstallationId: installationId,
                gateway: GrpcFeatureStudioGateway(client: client),
                exitCoordinator: studioExit,
                onBackToChat: (_, _) => context.go('/chat'),
                onRunNow: (authoritativeDraftId, expectedRevision) =>
                    context.go(
                      _resumeChatLocation(
                        featureDraftId: authoritativeDraftId,
                        expectedRevision: expectedRevision,
                        idempotencyId: nextRunNowId(),
                      ),
                    ),
              );
            },
          ),
          GoRoute(
            path: '/features/:featureId/releases/:releaseDigest',
            name: 'feature-version',
            builder: buildFeatureVersion,
          ),
          GoRoute(
            path: '/features/:featureId/versions/:releaseDigest',
            name: 'feature-version-legacy',
            builder: buildFeatureVersion,
          ),
          GoRoute(
            path: '/features/:featureId',
            name: 'feature-release',
            builder: (context, state) {
              final featureId = state.pathParameters['featureId']!;
              final client = AppSessionScope.of(context).digitalBrainClient;
              if (client == null) {
                return const Material(
                  child: Center(child: Text('Feature is unavailable.')),
                );
              }
              return FeatureReleasePage(
                key: ValueKey('feature-release-$featureId'),
                featureId: featureId,
                automationId: FeatureAutomationId.tryParse(
                  state.uri.queryParameters['automationId'],
                ),
                gateway: GrpcFeatureReleaseGateway(client: client),
              );
            },
          ),
        ],
      ),
    ],
  );
}

String _resumeChatLocation({
  required String featureDraftId,
  required Int64 expectedRevision,
  required String idempotencyId,
}) => Uri(
  path: '/chat',
  queryParameters: {
    'intent': 'resume-originating-request',
    'featureDraftId': featureDraftId,
    'expectedRevision': expectedRevision.toString(),
    'idempotencyId': idempotencyId,
  },
).toString();

String _activityChatLocation({String? conversationId, String? requestId}) {
  final queryParameters = <String, String>{};
  if (conversationId != null) {
    queryParameters['conversationId'] = conversationId;
  }
  if (requestId != null) {
    queryParameters['requestId'] = requestId;
  }
  return Uri(path: '/chat', queryParameters: queryParameters).toString();
}

String _featureAutomationLocation({
  required String featureId,
  required String automationId,
}) => Uri(
  path: '/features/$featureId',
  queryParameters: {'automationId': automationId},
).toString();

ResumeOriginatingRequestIntent? _resumeIntent(Uri uri) {
  final draftId = uri.queryParameters['featureDraftId'];
  final revisionText = uri.queryParameters['expectedRevision'];
  final idempotencyId = uri.queryParameters['idempotencyId'];
  final revision = revisionText == null ? null : BigInt.tryParse(revisionText);
  if (!_boundedCoordinate(draftId, 128) ||
      !_boundedCoordinate(idempotencyId, 256) ||
      revision == null ||
      revision <= BigInt.zero ||
      revision > BigInt.parse('9223372036854775807')) {
    return null;
  }
  return ResumeOriginatingRequestIntent(
    draftId: draftId!,
    expectedRevision: Int64.parseInt(revisionText!),
    idempotencyId: idempotencyId!,
  );
}

bool _boundedCoordinate(String? value, int maximumLength) =>
    value != null &&
    value.isNotEmpty &&
    value.length <= maximumLength &&
    value == value.trim();
