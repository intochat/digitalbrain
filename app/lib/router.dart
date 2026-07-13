import 'package:go_router/go_router.dart';

import 'runtime/widgets/runtime_shell.dart';

final digitalbrainRouter = GoRouter(
  initialLocation: '/chat',
  routes: [
    GoRoute(path: '/', redirect: (context, state) => '/chat'),
    GoRoute(
      path: '/chat',
      name: 'chat',
      builder: (context, state) => const RuntimeShell(),
    ),
  ],
);
