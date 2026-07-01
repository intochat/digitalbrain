import 'package:flutter/material.dart';
import 'package:forui/forui.dart';

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
      builder: (context, child) {
        final foruiTheme = FThemes.neutral.dark.desktop.copyWith(
          colors: FThemes.neutral.dark.desktop.colors.copyWith(
            background: DigitalBrainColors.pitchBlack,
            foreground: DigitalBrainColors.ink,
            card: DigitalBrainColors.obsidian,
          ),
        );
        return FTheme(
          data: foruiTheme,
          child: FToaster(
            child: FTooltipGroup(
              child: InputModeScope(
                child: WindowSizeScope(child: child ?? const SizedBox.shrink()),
              ),
            ),
          ),
        );
      },
      debugShowCheckedModeBanner: false,
    );
  }
}
