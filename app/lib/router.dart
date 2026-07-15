import 'package:go_router/go_router.dart';

import 'core/session/app_session_scope.dart';
import 'runtime/widgets/feature_proposal_placeholder.dart';
import 'runtime/widgets/chat_page.dart';
import 'runtime/widgets/runtime_shell.dart';
import 'shell/digitalbrain_shell.dart';

GoRouter createDigitalBrainRouter({String initialLocation = '/chat'}) =>
    GoRouter(
      initialLocation: initialLocation,
      routes: [
        GoRoute(path: '/', redirect: (context, state) => '/chat'),
        ShellRoute(
          builder: (context, state, child) => RuntimeShell(
            child: DigitalBrainShell(
              location: state.uri,
              onDestinationSelected: (destination) {
                if (destination.location != state.uri.path) {
                  context.go(destination.location);
                }
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
              builder: (context, state) => FeatureProposalPlaceholder(
                proposalId: state.pathParameters['proposalId']!,
              ),
            ),
          ],
        ),
      ],
    );
