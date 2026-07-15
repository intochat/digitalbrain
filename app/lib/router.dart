import 'package:go_router/go_router.dart';

import 'runtime/widgets/feature_proposal_placeholder.dart';
import 'runtime/widgets/chat_page.dart';
import 'runtime/widgets/runtime_shell.dart';

GoRouter createDigitalBrainRouter({String initialLocation = '/chat'}) =>
    GoRouter(
      initialLocation: initialLocation,
      routes: [
        GoRoute(path: '/', redirect: (context, state) => '/chat'),
        ShellRoute(
          builder: (context, state, child) => RuntimeShell(child: child),
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
