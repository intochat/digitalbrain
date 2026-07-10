import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'v2/widgets/v2_runtime_shell.dart';

final digitalbrainRouter = GoRouter(
  initialLocation: '/chat',
  routes: [
    GoRoute(path: '/', redirect: (context, state) => '/chat'),
    GoRoute(
      path: '/chat',
      name: 'chat',
      builder: (context, state) => const V2RuntimeShell(),
    ),
  ],
);
