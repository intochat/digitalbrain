import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/screens/brain/brain_home_screen.dart';
import 'package:ino_flutter/screens/chat/chat_screen.dart';
import 'package:ino_flutter/screens/onboarding/onboarding_screen.dart';
import 'package:ino_flutter/screens/persona/persona_screen.dart';
import 'package:ino_flutter/screens/rfw_v2_demo/rfw_v2_demo_screen.dart';
import 'package:ino_flutter/screens/rfw_v3_demo/rfw_v3_demo_screen.dart';
import 'package:ino_flutter/screens/shell/shell_screen.dart';

final _router = GoRouter(
  initialLocation: '/chat',
  redirect: (context, state) {
    if (state.uri.path == '/') {
      final q = state.uri.queryParameters['q'];
      if (q != null && q.isNotEmpty) {
        return '/chat?q=${Uri.encodeComponent(q)}';
      }
      return '/chat';
    }
    return null;
  },
  routes: [
    GoRoute(path: '/chat', builder: (context, state) => const ChatScreen()),
    GoRoute(path: '/brain', builder: (context, state) => const BrainHomeScreen()),
    GoRoute(path: '/onboarding', builder: (context, state) => const OnboardingScreen()),
    GoRoute(path: '/rfw-v2', builder: (context, state) => const RfwV2DemoScreen()),
    GoRoute(path: '/rfw-v3', builder: (context, state) => const RfwV3DemoScreen()),
    GoRoute(path: '/persona', builder: (context, state) => const PersonaScreen()),
    GoRoute(path: '/shell', builder: (context, state) => const ShellScreen()),
  ],
);

class InoApp extends StatelessWidget {
  const InoApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'ino',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF6C63FF),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      routerConfig: _router,
    );
  }
}
