import 'dart:async';
import 'dart:io';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

Future<void> main(List<String> args) async {
  final shell = _arg(args, '--shell') ?? DigitalBrainHostEnv.resolveShell();
  final open = _arg(args, '--open');
  final reconnectSeconds =
      int.tryParse(_arg(args, '--reconnect-seconds') ?? '') ?? 2;

  final client = DigitalBrainUiEdgeClient.fromEnvironment();
  final surface = ShellSurfaceController();

  stdout.writeln('digitalbrain_host shell=$shell base=${client.baseUri}');

  try {
    if (open != null) {
      final parts = open.split(':');
      if (parts.length < 2 || parts[0].isEmpty || parts[1].isEmpty) {
        stderr.writeln('usage: --open sceneKey:Title');
        exitCode = 64;
        return;
      }
      final sceneKey = parts[0];
      final title = parts.sublist(1).join(':');
      await _retry(
        label: 'open-scene',
        reconnectSeconds: reconnectSeconds,
        action: () => client.openScene(
          shellName: shell,
          sceneKey: sceneKey,
          title: title,
        ),
      );
      stdout.writeln('opened sceneKey=$sceneKey title=$title');
    }

    var afterSequence = 0;
    while (true) {
      try {
        await for (final event in client.watchShellEvents(
          shellName: shell,
          afterSequence: afterSequence,
        )) {
          final view = surface.apply(event);
          if (view.sequence > afterSequence) {
            afterSequence = view.sequence;
          }
          stdout.writeln(
            'scene-opened seq=${view.sequence} key=${view.sceneKey} title=${view.title}',
          );
        }
        stderr.writeln(
          'sse ended afterSequence=$afterSequence; reconnecting in ${reconnectSeconds}s',
        );
      } on Object catch (error, stack) {
        stderr.writeln(
          'sse error afterSequence=$afterSequence: $error; reconnecting in ${reconnectSeconds}s',
        );
        stderr.writeln('$stack');
      }
      await Future<void>.delayed(Duration(seconds: reconnectSeconds));
    }
  } finally {
    client.close();
  }
}

Future<void> _retry({
  required String label,
  required int reconnectSeconds,
  required Future<void> Function() action,
}) async {
  while (true) {
    try {
      await action();
      return;
    } on Object catch (error) {
      stderr.writeln('$label failed: $error; retry in ${reconnectSeconds}s');
      await Future<void>.delayed(Duration(seconds: reconnectSeconds));
    }
  }
}

String? _arg(List<String> args, String name) {
  final index = args.indexOf(name);
  if (index < 0 || index + 1 >= args.length) {
    return null;
  }
  return args[index + 1];
}
