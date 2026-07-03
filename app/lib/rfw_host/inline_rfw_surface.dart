import 'package:flutter/widgets.dart';
import 'package:rfw/rfw.dart' show RemoteEventHandler;

import 'rfw_runtime_host.dart';

Widget? buildInlineRfwSurface({
  required RfwRuntimeHost host,
  required Map<String, Object?> data,
  required String fallbackKey,
  required String defaultRootWidget,
  required RemoteEventHandler onEvent,
  String? correlationId,
  String? semanticsId,
}) {
  final source = data['source'] as String?;
  if (source == null || source.isEmpty) return null;

  final root =
      (data['rootWidget'] as String? ?? data['root'] as String? ?? defaultRootWidget);
  final key = (correlationId == null || correlationId.isEmpty) ? fallbackKey : correlationId;
  host.ensureLoaded(key, source);
  return SizedBox.expand(
    child: host.render(
      key,
      data: data,
      onEvent: onEvent,
      rootWidget: root,
      semanticsId: semanticsId,
    ),
  );
}
