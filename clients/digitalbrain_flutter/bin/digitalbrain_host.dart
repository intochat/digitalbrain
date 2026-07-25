import 'dart:io';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';

Future<void> main(List<String> args) async {
  final shell = _arg(args, '--shell') ?? DigitalBrainHostEnv.resolveShell();
  final open = _arg(args, '--open');

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
      await client.openScene(shellName: shell, sceneKey: sceneKey, title: title);
      stdout.writeln('opened sceneKey=$sceneKey title=$title');
    }

    await for (final event in client.watchShellEvents(shellName: shell)) {
      final view = surface.apply(event);
      stdout.writeln(
        'scene-opened seq=${view.sequence} key=${view.sceneKey} title=${view.title}',
      );
    }
  } finally {
    client.close();
  }
}

String? _arg(List<String> args, String name) {
  final index = args.indexOf(name);
  if (index < 0 || index + 1 >= args.length) {
    return null;
  }
  return args[index + 1];
}
