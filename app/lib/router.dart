import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import 'features/canvas/living_canvas_screen.dart';
import 'features/experience/experience_host_screen.dart';
import 'features/spike/globe_lottie_spike.dart';
import 'shell/forui_app_shell.dart';

final digitalbrainRouter = GoRouter(
  initialLocation: '/chat',
  routes: [
    GoRoute(path: '/', redirect: (context, state) => '/chat'),
    ShellRoute(
      builder: (context, state, child) => ForuiAppShell(child: child),
      routes: [
        GoRoute(
          path: '/chat',
          name: 'chat',
          builder: (context, state) =>
              const SizedBox.shrink(), // fully surface-driven via shell tree target 'chat'
        ),
        GoRoute(
          path: '/gallery',
          name: 'gallery',
          builder: (context, state) =>
              const SizedBox.shrink(), // UI kit gallery now emitted as rich neuron tree (forui components)
        ),
      ],
    ),
    // Canvas is advanced immersive (still uses some RFW surfaces but will be further thinned in future steps).
    GoRoute(
      path: '/canvas',
      name: 'canvas',
      builder: (context, state) => const LivingCanvasScreen(),
    ),
    GoRoute(
      path: '/experience',
      name: 'experience',
      builder: (context, state) => const ExperienceHostScreen(),
    ),
    GoRoute(
      path: '/experience/:pack/:experienceId',
      name: 'experience-targeted',
      builder: (context, state) => ExperienceHostScreen(
        pack: state.pathParameters['pack'],
        experienceId: state.pathParameters['experienceId'],
      ),
    ),
    GoRoute(
      path: '/spike',
      name: 'globe-lottie-spike',
      builder: (context, state) => const GlobeLottieSpike(),
    ),
  ],
);
