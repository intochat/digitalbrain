import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'core/session/app_session_scope.dart';
import 'features/studio/feature_studio_gateway.dart';
import 'features/studio/feature_studio_page.dart';
import 'runtime/widgets/chat_page.dart';
import 'runtime/widgets/runtime_shell.dart';
import 'shell/digitalbrain_shell.dart';

GoRouter createDigitalBrainRouter({String initialLocation = '/chat'}) {
  final studioExit = FeatureStudioExitCoordinator();
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
            builder: (context, state) => const ChatPage(),
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
              final client = AppSessionScope.of(context).digitalBrainClient;
              if (client == null) {
                return const Material(
                  child: Center(child: Text('Feature Studio is unavailable.')),
                );
              }
              return FeatureStudioPage(
                key: ValueKey('feature-studio-$draftId'),
                draftId: draftId,
                gateway: GrpcFeatureStudioGateway(client: client),
                exitCoordinator: studioExit,
                onBackToChat: () {
                  if (context.canPop()) {
                    context.pop();
                  } else {
                    context.go('/chat');
                  }
                },
              );
            },
          ),
        ],
      ),
    ],
  );
}
