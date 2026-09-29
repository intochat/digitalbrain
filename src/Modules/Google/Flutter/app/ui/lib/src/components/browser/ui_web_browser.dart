import 'package:flutter/material.dart';

import '../../models/ui_part.dart';
import '../../theme/ui_theme.dart';
import 'embedded_browser_stub.dart'
    if (dart.library.io) 'embedded_browser_windows.dart';

final class UiWebBrowser extends StatelessWidget {
  const UiWebBrowser({
    super.key,
    required this.part,
    this.height = 160,
    this.onConnect,
    this.onDisconnect,
  });

  final UiWebBrowserPart part;
  final double? height;
  final Future<void> Function(int, String)? onConnect;
  final Future<void> Function(String)? onDisconnect;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Semantics(
      label: 'browser ${part.title}',
      child: DecoratedBox(
        key: Key('ui_webbrowser_${part.name}'),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
          border: Border.all(color: scheme.outlineVariant),
        ),
        child: SizedBox(
          height: height,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  part.title,
                  style: TextStyle(
                    fontFamily: UiType.bodyFamily,
                    fontWeight: FontWeight.w600,
                    color: scheme.onSurface,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  part.uri,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: UiType.bodyFamily,
                    color: scheme.onSurfaceVariant,
                  ),
                ),
                Expanded(
                  child: EmbeddedBrowser(
                    onConnect: onConnect,
                    onDisconnect: onDisconnect,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
