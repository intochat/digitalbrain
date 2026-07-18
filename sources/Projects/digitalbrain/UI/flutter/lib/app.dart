import 'package:flutter/material.dart';

import 'digital_brain_ui/digital_brain_ui.dart';
import 'router.dart';
import 'theme/digitalbrain_theme.dart';

class DigitalBrainApp extends StatelessWidget {
  const DigitalBrainApp({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = buildDigitalBrainTheme();
    return MaterialApp.router(
      title: 'DigitalBrain',
      themeMode: ThemeMode.dark,
      theme: theme,
      darkTheme: theme,
      routerConfig: digitalbrainRouter,
      builder: (context, child) => InputModeScope(
        child: WindowSizeScope(child: child ?? const SizedBox.shrink()),
      ),
      debugShowCheckedModeBanner: false,
    );
  }
}
