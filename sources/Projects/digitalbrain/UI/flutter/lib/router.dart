import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

import 'features/canvas/living_canvas_screen.dart';
import 'features/spike/globe_lottie_spike.dart';

final digitalbrainRouter = GoRouter(
  initialLocation: '/',
  routes: [
    // Slice 0 throwaway de-risk page (docs/redesign). Delete after decision.
    GoRoute(
      path: '/spike',
      name: 'globe-lottie-spike',
      builder: (context, state) => const GlobeLottieSpike(),
    ),
    // Root landing: active living brain workspace (default to digitalbrain)
    GoRoute(
      path: '/',
      name: 'home-app',
      pageBuilder: (context, state) {
        return CustomTransitionPage(
          key: state.pageKey,
          child: CallbackShortcuts(
            bindings: <ShortcutActivator, VoidCallback>{
              const SingleActivator(LogicalKeyboardKey.escape): () {
                // Return to home dashboard
                context.go('/');
              },
            },
            child: Focus(autofocus: true, child: const LivingCanvasScreen()),
          ),
          transitionsBuilder: (context, animation, secondaryAnimation, child) {
            return FadeTransition(
              opacity: CurveTween(
                curve: Curves.easeInOutCirc,
              ).animate(animation),
              child: child,
            );
          },
        );
      },
    ),
  ],
);
