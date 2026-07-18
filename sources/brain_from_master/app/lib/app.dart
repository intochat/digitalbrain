import 'dart:async';

import 'package:flutter/material.dart';
import 'package:forui/forui.dart';
import 'package:go_router/go_router.dart';

import 'core/session/app_session_scope.dart';
import 'digital_brain_ui/digital_brain_ui.dart';
import 'router.dart';
import 'runtime/grpc_ui_transport.dart';
import 'runtime/runtime_session_owner.dart';
import 'theme/digitalbrain_theme.dart';

class DigitalBrainApp extends StatefulWidget {
  const DigitalBrainApp({
    super.key,
    this.sessionOwnerFactory,
    this.routerFactory,
  });

  final RuntimeSessionOwner Function()? sessionOwnerFactory;
  final GoRouter Function()? routerFactory;

  @override
  State<DigitalBrainApp> createState() => _DigitalBrainAppState();
}

class _DigitalBrainAppState extends State<DigitalBrainApp> {
  late final RuntimeSessionOwner _sessionOwner;
  late final GoRouter _router;

  @override
  void initState() {
    super.initState();
    _sessionOwner =
        widget.sessionOwnerFactory?.call() ??
        RuntimeSessionOwner(transportFactory: GrpcUiTransport.connect);
    _router = widget.routerFactory?.call() ?? createDigitalBrainRouter();
    scheduleMicrotask(_sessionOwner.initialize);
  }

  @override
  void dispose() {
    _router.dispose();
    unawaited(_sessionOwner.close());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = buildDigitalBrainTheme();
    return AppSessionScope(
      owner: _sessionOwner,
      child: MaterialApp.router(
        title: 'DigitalBrain',
        themeMode: ThemeMode.dark,
        theme: theme,
        darkTheme: theme,
        routerConfig: _router,
        builder: (context, child) {
          final foruiBaseTheme = FTheme.neutral.dark.desktop;
          final foruiTheme = FThemeData(
            colors: foruiBaseTheme.colors.copyWith(
              background: DigitalBrainColors.pitchBlack,
              foreground: DigitalBrainColors.ink,
              card: DigitalBrainColors.obsidian,
            ),
            touch: false,
          );
          return FTheme(
            data: foruiTheme,
            child: FToaster(
              child: FTooltipGroup(
                child: InputModeScope(
                  child: WindowSizeScope(
                    child: child ?? const SizedBox.shrink(),
                  ),
                ),
              ),
            ),
          );
        },
        debugShowCheckedModeBanner: false,
      ),
    );
  }
}
